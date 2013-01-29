using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using EventStore.ClientAPI;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HobbitES.Infrastructure
{
    public static class CommonDomainEventStoreRepositoryConfigurer
    {
        public static Configure EventStoreRepository(this Configure config)
        {
            config.Configurer.ConfigureComponent<CommonDomainEventStoreRepository>(DependencyLifecycle.InstancePerUnitOfWork)
                  .ConfigureProperty(r => r._eventStoreConnection, EventStoreConnection.Create())
                  .ConfigureProperty(r => r._tcpEndpoint, new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1113));
            return config;
        }
    }

    public class CommonDomainEventStoreRepository : IRepository
    {
        private const string EventClrTypeHeader = "EventClrTypeName";
        private const string AggregateClrTypeHeader = "AggregateClrTypeName";
        private const string CommitIdHeader = "CommitId";
        private const int WritePageSize = 500;
        private const int ReadPageSize = 500;

        private readonly Func<Type, Guid, string> _aggregateIdToStreamName;

        public EventStoreConnection _eventStoreConnection { get; set; }
        public IPEndPoint _tcpEndpoint { get; set; }

        public CommonDomainEventStoreRepository()
        {
            _aggregateIdToStreamName =
                (t, g) => string.Format("{0}-{1}", char.ToLower(t.Name[0]) + t.Name.Substring(1), g);
        }

        public CommonDomainEventStoreRepository(EventStoreConnection eventStoreConnection, IPEndPoint eventStoreTcpEndpoint)
            : this(eventStoreConnection, eventStoreTcpEndpoint, (t, g) => string.Format("{0}-{1}", char.ToLower(t.Name[0]) + t.Name.Substring(1), g))
        {
        }

        public CommonDomainEventStoreRepository(EventStoreConnection eventStoreConnection, IPEndPoint eventStoreTcpEndpoint, Func<Type, Guid, string> aggregateIdToStreamName)
        {
            _eventStoreConnection = eventStoreConnection;
            _tcpEndpoint = eventStoreTcpEndpoint;
            _aggregateIdToStreamName = aggregateIdToStreamName;
        }

        public TAggregate GetById<TAggregate>(Guid id) where TAggregate : IAggregate
        {
            EnsureConnected();

            var streamName = _aggregateIdToStreamName(typeof(TAggregate), id);
            var aggregate = ConstructAggregate<TAggregate>();

            StreamEventsSlice currentSlice;
            var nextSliceStart = 1;
            do
            {
                currentSlice = _eventStoreConnection.ReadStreamEventsForward(streamName, nextSliceStart, ReadPageSize, false);
                nextSliceStart = currentSlice.NextEventNumber;

                foreach (var evnt in currentSlice.Events)
                    aggregate.ApplyEvent(DeserializeEvent(evnt.OriginalEvent.Metadata, evnt.OriginalEvent.Data));
            } while (!currentSlice.IsEndOfStream);

            return aggregate;
        }

        public TAggregate GetById<TAggregate>(Guid id, int version) where TAggregate : IAggregate
        {
            EnsureConnected();

            var streamName = _aggregateIdToStreamName(typeof(TAggregate), id);
            var aggregate = ConstructAggregate<TAggregate>();

            var sliceStart = 1; //Ignores $StreamCreated
            StreamEventsSlice currentSlice;
            do
            {
                var sliceCount = sliceStart + ReadPageSize <= version
                                     ? ReadPageSize
                                     : version - sliceStart + 1;

                currentSlice = _eventStoreConnection.ReadStreamEventsForward(streamName, sliceStart, sliceCount, false);
                sliceStart = currentSlice.NextEventNumber;

                foreach (var evnt in currentSlice.Events)
                    aggregate.ApplyEvent(DeserializeEvent(evnt.OriginalEvent.Metadata, evnt.OriginalEvent.Data));
            } while (version > currentSlice.NextEventNumber && !currentSlice.IsEndOfStream);

            return aggregate;
        }

        public object DeserializeEvent(byte[] metadata, byte[] data)
        {
            var eventClrTypeName = JObject.Parse(Encoding.UTF8.GetString(metadata)).Property(EventClrTypeHeader).Value;
            return JsonConvert.DeserializeObject(Encoding.UTF8.GetString(data), Type.GetType((string)eventClrTypeName));
        }

        public void Save(IAggregate aggregate, Guid commitId, Action<IDictionary<string, object>> updateHeaders)
        {
            EnsureConnected();

            var commitHeaders = new Dictionary<string, object>
                                    {
                                        {CommitIdHeader, commitId},
                                        {AggregateClrTypeHeader, aggregate.GetType().AssemblyQualifiedName}
                                    };
            updateHeaders(commitHeaders);

            var streamName = _aggregateIdToStreamName(aggregate.GetType(), aggregate.Id);
            var newEvents = aggregate.GetUncommittedEvents().Cast<object>().ToList();
            var originalVersion = aggregate.Version - newEvents.Count;
            var expectedVersion = originalVersion == 0 ? -1 : originalVersion;
            var preparedEvents = PrepareEvents(newEvents, commitHeaders).ToList();

            if (preparedEvents.Count < WritePageSize)
            {
                _eventStoreConnection.AppendToStream(streamName, expectedVersion, preparedEvents);
            }
            else
            {
                var transaction = _eventStoreConnection.StartTransaction(streamName, expectedVersion);

                var position = 0;
                while (position < preparedEvents.Count)
                {
                    var pageEvents = preparedEvents.Skip(position).Take(WritePageSize);
                    transaction.Write(pageEvents);
                    position += WritePageSize;
                }

                transaction.Commit();
            }

            aggregate.ClearUncommittedEvents();
        }

        private static IEnumerable<EventData> PrepareEvents(IEnumerable<object> events, IDictionary<string, object> commitHeaders)
        {
            return events.Select(e => JsonEventData.Create(Guid.NewGuid(), e, commitHeaders));
        }

        private static TAggregate ConstructAggregate<TAggregate>()
        {
            return (TAggregate)Activator.CreateInstance(typeof(TAggregate), true);
        }

        private bool _isConnected;

        private void EnsureConnected()
        {
            if (_isConnected)
                return;

            _eventStoreConnection.Connect(_tcpEndpoint);
            _isConnected = true;
        }

        private static class JsonEventData
        {
            public static EventData Create(Guid eventId, object evnt, IDictionary<string, object> headers)
            {
                var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(evnt, SerializerSettings));
                var metadata = AddEventClrTypeHeaderAndSerializeMetadata(evnt, headers);
                var typeName = evnt.GetType().Name;

                return new EventData(eventId, typeName, true, data, metadata);
            }

            private static readonly JsonSerializerSettings SerializerSettings;

            private static byte[] AddEventClrTypeHeaderAndSerializeMetadata(object evnt, IDictionary<string, object> headers)
            {
                var eventHeaders = new Dictionary<string, object>(headers)
                                       {
                                           {EventClrTypeHeader, evnt.GetType().AssemblyQualifiedName}
                                       };

                return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(eventHeaders, SerializerSettings));
            }

            static JsonEventData()
            {
                SerializerSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None };
            }
        }
    }

    public interface IRepository
    {
        TAggregate GetById<TAggregate>(Guid id) where TAggregate : IAggregate;
        TAggregate GetById<TAggregate>(Guid id, int version) where TAggregate : IAggregate;
        object DeserializeEvent(byte[] metadata, byte[] data);
        void Save(IAggregate aggregate, Guid commitId, Action<IDictionary<string, object>> updateHeaders);
    }
}
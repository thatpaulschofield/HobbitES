using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using EventStore.ClientAPI;
using HobbitES.Messages;
using Newtonsoft.Json;

namespace HobbitES.Infrastructure
{
    namespace CommonDomain.Persistence.GetEventStore
    {
        public class GetEventStoreRepository
        {
            private const string EventClrTypeHeader = "EventClrTypeName";
            private const string AggregateClrTypeHeader = "AggregateClrTypeName";
            private const string CommitIdHeader = "CommitId";
            private const int WritePageSize = 500;
            private const int ReadPageSize = 500;

            private readonly Func<Type, Guid, string> _aggregateIdToStreamName;

            private readonly EventStoreConnection _eventStoreConnection;
            private readonly IPEndPoint _tcpEndpoint;

            public GetEventStoreRepository(EventStoreConnection eventStoreConnection, IPEndPoint eventStoreTcpEndpoint)
                : this(eventStoreConnection, eventStoreTcpEndpoint, (t, g) => string.Format("{0}-{1}", char.ToLower(t.Name[0]) + t.Name.Substring(1), g))
            {
            }

            public GetEventStoreRepository(EventStoreConnection eventStoreConnection, IPEndPoint eventStoreTcpEndpoint, Func<Type, Guid, string> aggregateIdToStreamName)
            {
                _eventStoreConnection = eventStoreConnection;
                _tcpEndpoint = eventStoreTcpEndpoint;
                _aggregateIdToStreamName = aggregateIdToStreamName;
            }

            public TAggregate GetById<TAggregate>(Guid id) where TAggregate : class, IAggregate
            {
                EnsureConnected();

                var streamName = _aggregateIdToStreamName(typeof(TAggregate), id);
                var aggregate = ConstructAggregate<TAggregate>();

                StreamEventsSlice currentSlice;
                var nextSliceStart = 1;
                do
                {
                    currentSlice = _eventStoreConnection.ReadStreamEventsForward(streamName, nextSliceStart, ReadPageSize, true);
                    nextSliceStart = currentSlice.NextEventNumber;

                    foreach (var evnt in currentSlice.Events)
                        aggregate.ApplyEvent(DeserializeEvent(evnt.Event.Metadata, evnt.Event.Data));
                } while (!currentSlice.IsEndOfStream);

                return aggregate;
            }

            public TAggregate GetById<TAggregate>(Guid id, int version) where TAggregate : class, IAggregate
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

                    currentSlice = _eventStoreConnection.ReadStreamEventsForward(streamName, sliceStart, sliceCount, true);
                    sliceStart = currentSlice.NextEventNumber;

                    foreach (var evnt in currentSlice.Events)
                        aggregate.ApplyEvent(DeserializeEvent(evnt.Event.Metadata, evnt.Event.Data));
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
                        _eventStoreConnection.AppendToStream(streamName, expectedVersion, pageEvents);
                        position += WritePageSize;
                    }
                    transaction.Commit();
                }

                aggregate.ClearUncommittedEvents();
            }

            private static IEnumerable<EventData> PrepareEvents(IEnumerable<object> events, IDictionary<string, object> commitHeaders)
            {
                return events.Select(e => PrepareEvent(new JsonAggregateEvent(Guid.NewGuid(), e, commitHeaders)));
            }

            private static EventData PrepareEvent(JsonAggregateEvent jsonAggregateEvent)
            {
                return new EventData(jsonAggregateEvent.EventId, jsonAggregateEvent.Type, true, jsonAggregateEvent.Data, jsonAggregateEvent.Metadata);
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

            private class JsonAggregateEvent : IEvent
            {
                public Guid EventId { get; private set; }
                public string Type { get; private set; }
                public bool IsJson { get; private set; }
                public byte[] Data { get; private set; }
                public byte[] Metadata { get; private set; }

                private static readonly JsonSerializerSettings SerializerSettings;

                public JsonAggregateEvent(Guid eventId, object evnt, IDictionary<string, object> headers)
                {
                    EventId = eventId;
                    Type = evnt.GetType().Name;
                    IsJson = true;
                    Data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(evnt, SerializerSettings));
                    Metadata = AddEventClrTypeHeaderAndSerializeMetadata(evnt, headers);
                }

                private static byte[] AddEventClrTypeHeaderAndSerializeMetadata(object evnt, IDictionary<string, object> headers)
                {
                    var eventHeaders = new Dictionary<string, object>(headers)
                    {
                        {EventClrTypeHeader, evnt.GetType().AssemblyQualifiedName}
                    };

                    return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(eventHeaders, SerializerSettings));
                }

                static JsonAggregateEvent()
                {
                    SerializerSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None };
                }
            }
        }
    }
}
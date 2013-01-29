using System;
using System.Collections.Generic;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NServiceBus.Saga;

namespace HobbitES.Infrastructure
{
    public static class ConfigureEventStoreSagaPersister
    {
        public static Configure EventStoreSagaPersistence(this Configure config)
        {
            config.Configurer
                .ConfigureComponent<EventStoreSagaPersister>(DependencyLifecycle.InstancePerCall);
            return config;
        }
    }

    public class EventStoreSagaPersister : ISagaPersister
    {
        private readonly IRepository _repository;
        private Action<IDictionary<string, object>> _updateHeaders = x => { };

        public EventStoreSagaPersister(IRepository repository)
        {
            _repository = repository;
        }

        public void Save(ISagaEntity saga)
        {
            _repository.Save(saga as IAggregate, Guid.NewGuid(), _updateHeaders);
        }

        public void Update(ISagaEntity saga)
        {
            
        }

        public T Get<T>(Guid sagaId) where T : ISagaEntity
        {
            //TODO: Cache method infos
            var getMethod = _repository.GetType()
                .GetMethod("GetById", new[]{typeof(Guid)}).MakeGenericMethod(typeof (T));
            var existingSaga = (T)getMethod.Invoke(_repository, new object[] { sagaId });
            if (existingSaga != null)
                return existingSaga;
            else
                return (T)Activator.CreateInstance(typeof(T));
            //return _repository.GetById<T>(sagaId);
        }

        public T Get<T>(string property, object value) where T : ISagaEntity
        {
            return default(T);
        }

        public void Complete(ISagaEntity saga)
        {
            
        }
    }
}
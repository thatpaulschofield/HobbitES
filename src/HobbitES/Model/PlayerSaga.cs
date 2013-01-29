using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HobbitES.Infrastructure;
using HobbitES.Messages;
using HobbitES.Messages.Commands;
using HobbitES.Messages.Events;
using NServiceBus.Saga;

namespace HobbitES.Model
{
    public class PlayerSaga : Saga<PlayerSagaData>,
        IAmStartedByMessages<RegisterPlayer>
    {
        public void Handle(RegisterPlayer message)
        {
            this.Data.Handle(message);
        }

        public override void ConfigureHowToFindSaga()
        {
            ConfigureMapping<RegisterPlayer>(sd => sd.Id, m => m.Id);
        }
    }

    public class PlayerSagaData : Aggregate, IContainSagaData
    {
        public string Originator { get; set; }
        public string OriginalMessageId { get; set; }

        public void Handle(RegisterPlayer message)
        {
            base.ApplyEvent(new PlayerRegistered(message.Id, message.PlayerId));
        }

    }
}

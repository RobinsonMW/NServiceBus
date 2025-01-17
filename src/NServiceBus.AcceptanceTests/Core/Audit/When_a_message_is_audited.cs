﻿namespace NServiceBus.AcceptanceTests.Audit
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using AcceptanceTesting.Customization;
    using EndpointTemplates;
    using MessageMutator;
    using NServiceBus.Audit;
    using NServiceBus.Pipeline;
    using NServiceBus.Routing;
    using NServiceBus.Transport;
    using NUnit.Framework;

    public class When_a_message_is_being_audited : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_allow_audit_action_to_be_replaced()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<EndpointWithSeparateBodyStorage>(b => b.When((session, c) => session.SendLocal(new MessageToBeAudited())))
                .WithEndpoint<AuditSpyEndpoint>()
                .Done(c => c.AuditMessageReceived)
                .Run();

            Assert.True(context.BodyWasEmpty);
        }

        public class Context : ScenarioContext
        {
            public bool AuditMessageReceived { get; set; }
            public bool BodyWasEmpty { get; set; }
        }

        public class EndpointWithSeparateBodyStorage : EndpointConfigurationBuilder
        {
            public EndpointWithSeparateBodyStorage()
            {
                EndpointSetup<DefaultServer, Context>((config, context) =>
                 {
                     config.AuditProcessedMessagesTo<AuditSpyEndpoint>();
                     config.Pipeline.Register(typeof(AuditBodyStorageBehavior), "Simulate writing the body to a separate storage and pass a null body to the transport");
                 });
            }

            public class AuditBodyStorageBehavior : Behavior<IAuditContext>
            {
                public override Task Invoke(IAuditContext context, Func<Task> next)
                {
                    //body, headers and metadata can be stored separately here

                    context.AuditAction = new ExcludeBodyFromAuditedMessage();
                    return next();
                }

                class ExcludeBodyFromAuditedMessage : AuditAction
                {
                    public override IReadOnlyCollection<IRoutingContext> GetRoutingContexts(IAuditActionContext context)
                    {
                        var processedMessage = context.Message;

                        //simulate the body being stored in eg. blobstorage already
                        var auditMessage = new OutgoingMessage(processedMessage.MessageId, processedMessage.Headers, ReadOnlyMemory<byte>.Empty);

                        return new[] { context.CreateRoutingContext(auditMessage, new UnicastRoutingStrategy(context.AuditAddress)) };
                    }
                }
            }

            public class MessageToBeAuditedHandler : IHandleMessages<MessageToBeAudited>
            {
                public Task Handle(MessageToBeAudited message, IMessageHandlerContext context)
                {
                    return Task.CompletedTask;
                }
            }
        }

        class AuditSpyEndpoint : EndpointConfigurationBuilder
        {
            public AuditSpyEndpoint()
            {
                EndpointSetup<DefaultServer, Context>((config, context) => config.RegisterMessageMutator(new BodySpy(context)));
            }

            class BodySpy : IMutateIncomingTransportMessages
            {
                public BodySpy(Context context)
                {
                    this.context = context;
                }

                public Task MutateIncoming(MutateIncomingTransportMessageContext transportMessage)
                {
                    context.BodyWasEmpty = transportMessage.Body.Length == 0;
                    context.AuditMessageReceived = true;
                    return Task.CompletedTask;
                }

                Context context;
            }
        }

        public class MessageToBeAudited : IMessage
        {
        }
    }
}
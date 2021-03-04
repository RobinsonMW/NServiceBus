﻿namespace NServiceBus.TransportTests
{
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Transport;

    public class When_stop_cancelled_on_message : NServiceBusTransportTest
    {
        [TestCase(TransportTransactionMode.None)]
        [TestCase(TransportTransactionMode.ReceiveOnly)]
        [TestCase(TransportTransactionMode.SendsAtomicWithReceive)]
        [TestCase(TransportTransactionMode.TransactionScope)]
        public async Task Should_not_invoke_recoverability(TransportTransactionMode transactionMode)
        {
            var recoverabilityInvoked = false;

            var messageProcessingStarted = new TaskCompletionSource<bool>();
            var completed = new TaskCompletionSource<bool>();

            OnTestTimeout(() =>
            {
                messageProcessingStarted.SetCanceled();
                completed.SetCanceled();
            });

            await StartPump(
                async (_, cancellationToken) =>
                {
                    messageProcessingStarted.SetResult(true);
                    await Task.Delay(TestTimeout, cancellationToken);
                },
                (_, __) =>
                {
                    recoverabilityInvoked = true;
                    return Task.FromResult(ReceiveResult.Discarded);
                },
                (_, __) => completed.SetCompleted(),
                transactionMode);

            await SendMessage(InputQueueName);

            _ = await messageProcessingStarted.Task;

            await StopPump(new CancellationToken(true));

            _ = await completed.Task;

            Assert.False(recoverabilityInvoked, "Recoverability should not have been invoked.");
        }
    }
}

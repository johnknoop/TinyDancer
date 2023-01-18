using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using FluentAssertions;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using Xunit;

namespace IntegrationTests
{
	public class TransactionTests
	{
		async Task WithTransactionAsync(Func<Task> transactionBody, bool failInPreparePhase, bool failInCommitPhase)
		{
			int preparePhaseAttempts = 0;
			int commitPhaseAttempts = 0;

			await Retryer.RetryAsync(async () => {
				preparePhaseAttempts++;

				using (var trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
				{
					var enlistment = new RetryingTransactionEnlistment(() =>
					{
						commitPhaseAttempts++;

						// chance to throw in commit phase
						if (failInCommitPhase && commitPhaseAttempts < 2)
						{
							throw new MySpecialException();
						}
					});

					Transaction.Current.EnlistVolatile(enlistment, EnlistmentOptions.None);

					await transactionBody();

					if (failInPreparePhase && preparePhaseAttempts < 2)
					{
						throw new MySpecialException();
					}

					trans.Complete();
				}
			});
		}

		[Fact]
		public async Task WhenMessageSentInTransaction_TransactionBodyFails_ShouldOnlyBeSentWhenTransactionSuccesfullyCommitted()
		{
			var client = new ServiceBusClient("Endpoint=sb://merkenda.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=1fnXhoLA4dS4cwQAa64KW3fnqdO9Cd5FjjCZpVyw4H4=");
			var managementClient = new ServiceBusAdministrationClient("Endpoint=sb://merkenda.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=1fnXhoLA4dS4cwQAa64KW3fnqdO9Cd5FjjCZpVyw4H4=");

			var queueStateBefore = await managementClient.GetQueueRuntimePropertiesAsync("inputqueue");

			var messageSender = client.CreateSender("inputqueue");

			await WithTransactionAsync(async () =>
			{
				await messageSender.SendMessageAsync(new ServiceBusMessage
				{
					Body = new BinaryData(Encoding.UTF8.GetBytes("test message")),
					SessionId = "session"
				});
			}, true, false);

			var queueStateAfter = await managementClient.GetQueueRuntimePropertiesAsync("inputqueue");

			queueStateAfter.Value.ActiveMessageCount.Should().Be(queueStateBefore.Value.ActiveMessageCount + 1);
		}

		[Fact]
		public async Task WhenMessageSentInTransaction_TransactionCommitmentFails_ShouldOnlyBeSentWhenTransactionSuccesfullyCommitted()
		{
			var client = new ServiceBusClient("Endpoint=sb://merkenda.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=1fnXhoLA4dS4cwQAa64KW3fnqdO9Cd5FjjCZpVyw4H4=");
			var managementClient = new ServiceBusAdministrationClient("Endpoint=sb://merkenda.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=1fnXhoLA4dS4cwQAa64KW3fnqdO9Cd5FjjCZpVyw4H4=");

			var queueStateBefore = await managementClient.GetQueueRuntimePropertiesAsync("inputqueue");

			var messageSender = client.CreateSender("inputqueue");

			await WithTransactionAsync(async () =>
			{
				await messageSender.SendMessageAsync(new ServiceBusMessage
				{
					Body = new BinaryData(Encoding.UTF8.GetBytes("test message")),
					SessionId = "session"
				});
			}, false, true);

			var queueStateAfter = await managementClient.GetQueueRuntimePropertiesAsync("inputqueue");

			queueStateAfter.Value.ActiveMessageCount.Should().Be(queueStateBefore.Value.ActiveMessageCount + 1);
		}
	}

	public class MySpecialException : Exception
	{

	}

	public class RetryingTransactionEnlistment : IEnlistmentNotification
	{
		private readonly int _maxRetries;

		public Action OnCommit { get; private set; }

		public RetryingTransactionEnlistment(Action onCommit)
		{
			OnCommit = onCommit;
		}

		public void Commit(Enlistment enlistment)
		{
			Retryer.RetryAsync(() => {
				OnCommit();
				return Task.CompletedTask;
			}, _maxRetries).Wait();
			enlistment.Done();
		}

		public void InDoubt(Enlistment enlistment)
		{
			enlistment.Done();
		}

		public void Prepare(PreparingEnlistment preparingEnlistment)
		{
			preparingEnlistment.Prepared();
		}

		public void Rollback(Enlistment enlistment)
		{
			enlistment.Done();
		}
	}

	internal static class Retryer
	{
		private static readonly TimeSpan TransactionTimeout = TimeSpan.FromSeconds(120);

		public static async Task RetryAsync(Func<Task> transactionBody, int maxRetries = default)
		{
			await RetryAsync<object>(async () => {
				await transactionBody();
				return null;
			}, maxRetries);
		}

		public static async Task<TReturnType> RetryAsync<TReturnType>(Func<Task<TReturnType>> transactionBody, int maxRetries = default)
		{
			var tries = 0;
			var startTime = DateTime.UtcNow;

			while (true)
			{
				try
				{
					var result = await transactionBody();
					return result;
				}
				catch (MySpecialException)
				{
					if (maxRetries != default && tries >= maxRetries && DateTime.UtcNow - startTime < TransactionTimeout)
					{
						throw;
					}

					tries++;
				}
			}
		}
	}
}

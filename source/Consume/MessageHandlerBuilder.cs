using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using Zwiftly.SharedLibrary.ServiceBusExtensions.Consume.Session;

namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public class MessageHandlerBuilder
	{
		private readonly IReceiverClient _receiverClient;
		private readonly Configuration _config;

		private Func<IReceiverClient, Message, Task> _unrecognizedMessageHandler = null;
		private ExceptionHandler _deserializationErrorHandler = null;
		private Action<ExceptionReceivedEventArgs> _receiverActionCallback = null;

		private readonly Dictionary<Type, ExceptionHandler> _exceptionHandlers = new Dictionary<Type, ExceptionHandler>();
		private ExceptionHandler _unhandledExceptionHandler = null;

		private readonly Dictionary<string, (Type type, Func<object, Task> handler)> _messageHandlers = new Dictionary<string, (Type type, Func<object, Task> handler)>();
		private (Type type, Func<object, Task> handler)? _globalHandler = null;

		public MessageHandlerBuilder(IReceiverClient receiverClient, Configuration config)
		{
			_receiverClient = receiverClient;
			_config = config;
		}

		public MessageHandlerBuilder Catch<TException>(Func<ExceptionHandlingBuilder<TException>, ExceptionHandler<TException>> handleStrategy, ExceptionCallback<TException> callback = null) where TException : Exception
		{
			var handler = handleStrategy(new ExceptionHandlingBuilder<TException>());

			_exceptionHandlers[typeof(TException)] = async (client, message, ex) =>
			{
				await handler(client, message, (TException)ex);
				callback?.Invoke(message, (TException)ex);
			};

			return this;
		}

		public MessageHandlerBuilder CatchUnhandledExceptions(Func<ExceptionHandlingBuilder<Exception>, ExceptionHandler<Exception>> handleStrategy, ExceptionCallback<Exception> callback = null)
		{
			var handler = handleStrategy(new ExceptionHandlingBuilder<Exception>());

			_unhandledExceptionHandler = async (client, message, ex) =>
			{
				await handler(client, message, ex);
				callback?.Invoke(message, ex);
			};

			return this;
		}

		public MessageHandlerBuilder OnDeserializationFailed(Func<ExceptionHandlingBuilder<Exception>, ExceptionHandler<Exception>> handleStrategy, ExceptionCallback<Exception> callback = null)
		{
			var handler = handleStrategy(new ExceptionHandlingBuilder<Exception>());

			_deserializationErrorHandler = async (client, message, ex) =>
			{
				await handler(client, message, ex);
				callback?.Invoke(message, ex);
			};

			return this;
		}

		public MessageHandlerBuilder OnUnrecognizedMessageType(Func<UnrecognizedMessageHandlingBuilder, UnrecognizedMessageHandler> handleStrategy, Action<Message> callback = null)
		{
			var handler = handleStrategy(new UnrecognizedMessageHandlingBuilder());

			_unrecognizedMessageHandler = async (client, message) =>
			{
				await handler(client, message);
				callback?.Invoke(message);
			};

			return this;
		}

		public MessageHandlerBuilder OnReceiverException(Action<ExceptionReceivedEventArgs> callback)
		{
			_receiverActionCallback = callback;

			return this;
		}

		public MessageHandlerBuilder HandleMessage<TMessage>(AsyncMessageHandler<TMessage> action)
		{
			_messageHandlers[typeof(TMessage).Name] = (typeof(TMessage), async (message) =>
			{
				await action((TMessage)message);
			});

			return this;
		}

		public MessageHandlerBuilder HandleMessage<TMessage>(MessageHandler<TMessage> action)
		{
			_messageHandlers[typeof(TMessage).Name] = (typeof(TMessage), async (message) =>
			{
				action((TMessage) message);
				await Task.Yield();
			});

			return this;
		}

		public MessageHandlerBuilder HandleAllAs<T>(Action<T> handler)
		{
			_globalHandler = (typeof(T), async msg =>
			{
				handler((T) msg);
				await Task.Yield();
			});

			return this;
		}

		public MessageHandlerBuilder HandleAllAs<T>(Func<T, Task> handler)
		{
			_globalHandler = (typeof(T), async msg => await handler((T) msg));
			return this;
		}

		public void Subscribe(CancellationToken? cancelled, Func<IDisposable> dontInterrupt)
			=> DoSubscribe(cancelled, dontInterrupt);

		public void Subscribe()
			=> DoSubscribe();

		private void DoSubscribe(CancellationToken? cancelled = null, Func<IDisposable> dontInterrupt = null)
		{
			if (_unrecognizedMessageHandler != null && _globalHandler != null)
			{
				throw new InvalidOperationException($"{nameof(HandleAllAs)} and {nameof(OnUnrecognizedMessageType)} cannot both be used");
			}

			// todo: vakta mot typer med samma namn

			var blockInterruption = dontInterrupt ?? (() => new DummyDisposable());

			var messageHandlerOptions = new MessageHandlerOptions(exceptionEventArgs =>
			{
				_receiverActionCallback?.Invoke(exceptionEventArgs); // todo: implementera async version också
				return Task.CompletedTask;
			})
			{
				AutoComplete = false
			};

			if (_config?.MaxConcurrentMessages != null)
			{
				messageHandlerOptions.MaxConcurrentCalls = _config.MaxConcurrentMessages.Value;
			}

			if (_config?.MaxAutoRenewDuration != null)
			{
				messageHandlerOptions.MaxAutoRenewDuration = _config.MaxAutoRenewDuration.Value;
			}

			object Deserialize(Message message, Type type)
			{
				if (message.UserProperties.ContainsKey("compressed"))
				{
					return MessagePackSerializer.NonGeneric.Deserialize(type, message.Body, MessagePack.Resolvers.ContractlessStandardResolver.Instance);
				}
				else
				{
					return message.Body.Deserialize(type);
				}
			}

			_receiverClient.RegisterMessageHandler(async (message, cancel) =>
			{
				if (cancelled?.IsCancellationRequested == true)
				{
					await _receiverClient.AbandonAsync(message.SystemProperties.LockToken);
					return;
				}

				using (blockInterruption())
				{
					try
					{
						if (message.UserProperties.TryGetValue("MessageType", out var messageType) &&
							_messageHandlers.TryGetValue((string) messageType, out var y))
						{
							var deserialized = Deserialize(message, y.type);
							await y.handler(deserialized);
						}
						else if (_globalHandler != null)
						{
							var deserialized = Deserialize(message, _globalHandler.Value.type);
							await _globalHandler.Value.handler(deserialized);
						}
						else if (_unrecognizedMessageHandler != null)
						{
							await _unrecognizedMessageHandler(_receiverClient, message);
							return;
						}

						await _receiverClient.CompleteAsync(message.SystemProperties.LockToken);
					}
					catch (DeserializationFailedException ex)
					{
						await _deserializationErrorHandler(_receiverClient, message, ex.InnerException);
					}
					catch (Exception ex) when (_exceptionHandlers.Keys.Contains(ex.GetType()))
					{
						await _exceptionHandlers[ex.GetType()](_receiverClient, message, ex);
					}
					catch (Exception ex) when (_unhandledExceptionHandler != null)
					{
						await _unhandledExceptionHandler(_receiverClient, message, ex);
					}
				}

			}, messageHandlerOptions);
		}
	}
}
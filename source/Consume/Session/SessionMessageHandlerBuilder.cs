using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Azure.ServiceBus;

namespace TinyDancer.Consume.Session
{
	public class SessionMessageHandlerBuilder
	{
		private readonly SessionConfiguration _config;
		private readonly SessionClientWrapper _client;
		private readonly Dictionary<Type, SessionExceptionHandler> _exceptionHandlers = new Dictionary<Type, SessionExceptionHandler>();
		private SessionExceptionHandler _unhandledExceptionHandler = null;
		private SessionExceptionHandler _deserializationErrorHandler = null;
		
		private Func<IMessageSession, Message, Task> _unrecognizedMessageHandler = null;

		private readonly Dictionary<string, (Type type, Func<object, string, Task> handler)> _messageHandlers = new Dictionary<string, (Type type, Func<object, string, Task> handler)>();

		private Action<ExceptionReceivedEventArgs> _receiverActionCallback = null;
		private (Type type, Func<object, Task> handler)? _globalHandler = null;

		public SessionMessageHandlerBuilder(ISubscriptionClient client, SessionConfiguration config)
		{
			_config = config;
			_client = new SessionClientWrapper(client);
		}

		public SessionMessageHandlerBuilder(IQueueClient client, SessionConfiguration config)
		{
			_config = config;
			_client = new SessionClientWrapper(client);
		}

		public SessionMessageHandlerBuilder Catch<TException>(Func<SessionExceptionHandlingBuilder<TException>, SessionExceptionHandler<TException>> handleStrategy, SessionExceptionCallback<TException> callback = null) where TException : Exception
		{
			var handler = handleStrategy(new SessionExceptionHandlingBuilder<TException>());

			_exceptionHandlers[typeof(TException)] = async (session, message, ex) =>
			{
				await handler(session, message, (TException)ex);
				callback?.Invoke(message, (TException)ex, session.SessionId);
			};

			return this;
		}

		public SessionMessageHandlerBuilder CatchUnhandledExceptions(Func<SessionExceptionHandlingBuilder<Exception>, SessionExceptionHandler<Exception>> handleStrategy, SessionExceptionCallback<Exception> callback = null)
		{
			var handler = handleStrategy(new SessionExceptionHandlingBuilder<Exception>());

			_unhandledExceptionHandler = async (session, message, ex) =>
			{
				await handler(session, message, ex);
				callback?.Invoke(message, ex, session.SessionId);
			};

			return this;
		}

		public SessionMessageHandlerBuilder OnDeserializationFailed(Func<SessionExceptionHandlingBuilder<Exception>, SessionExceptionHandler<Exception>> handleStrategy, SessionExceptionCallback<Exception> callback = null)
		{
			var handler = handleStrategy(new SessionExceptionHandlingBuilder<Exception>());

			_deserializationErrorHandler = async (session, message, ex) =>
			{
				await handler(session, message, ex);
				callback?.Invoke(message, ex, session.SessionId);
			};

			return this;
		}

		public SessionMessageHandlerBuilder OnUnrecognizedMessageType(Func<UnrecognizedSessionMessageHandlingBuilder, UnrecognizedSessionMessageHandler> handleStrategy, Action<Message> callback = null)
		{
			var handler = handleStrategy(new UnrecognizedSessionMessageHandlingBuilder());

			_unrecognizedMessageHandler = async (session, message) =>
			{
				await handler(session, message);
				callback?.Invoke(message);
			};

			return this;
		}

		public SessionMessageHandlerBuilder OnReceiverException(Action<ExceptionReceivedEventArgs> callback)
		{
			_receiverActionCallback = callback;

			return this;
		}

		public SessionMessageHandlerBuilder HandleMessage<TMessage>(AsyncSessionMessageHandler<TMessage> action)
		{
			_messageHandlers[typeof(TMessage).Name] = (typeof(TMessage), async (message, sessionId) =>
			{
				await action((TMessage)message, sessionId);
			});

			return this;
		}

		public SessionMessageHandlerBuilder HandleMessage<TMessage>(SessionMessageHandler<TMessage> action)
		{
			_messageHandlers[typeof(TMessage).Name] = (typeof(TMessage), async (message, sessionId) =>
			{
				action((TMessage) message, sessionId);
				await Task.Yield();
			});

			return this;
		}

		public SessionMessageHandlerBuilder HandleAllAs<T>(Action<T> handler)
		{
			_globalHandler = (typeof(T), async msg =>
			{
				handler((T) msg);
				await Task.Yield();
			});

			return this;
		}

		public SessionMessageHandlerBuilder HandleAllAs<T>(Func<T, Task> handler)
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

			var messageHandlerOptions = new SessionHandlerOptions(exceptionEventArgs =>
			{
				_receiverActionCallback?.Invoke(exceptionEventArgs); // todo: implementera async version också
				return Task.CompletedTask;
			})
			{
				AutoComplete = false
			};

			if (_config?.MaxConcurrentSessions != null)
			{
				messageHandlerOptions.MaxConcurrentSessions = _config.MaxConcurrentSessions.Value;
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

			_client.RegisterSessionHandler(async (session, message, cancel) =>
			{
				if (cancelled?.IsCancellationRequested == true)
				{
					await session.AbandonAsync(message.SystemProperties.LockToken);
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
							await y.handler(deserialized, session.SessionId);
						}
						else if (_globalHandler != null)
						{
							var deserialized = Deserialize(message, _globalHandler.Value.type);
							await _globalHandler.Value.handler(deserialized);
						}
						else if (_unrecognizedMessageHandler != null)
						{
							await _unrecognizedMessageHandler(session, message);
							return;
						}

						await session.CompleteAsync(message.SystemProperties.LockToken);
					}
					catch (DeserializationFailedException ex)
					{
						await _deserializationErrorHandler(session, message, ex.InnerException);
					}
					catch (Exception ex) when (_exceptionHandlers.Keys.Contains(ex.GetType()))
					{
						await _exceptionHandlers[ex.GetType()](session, message, ex);
					}
					catch (Exception ex) when (_unhandledExceptionHandler != null)
					{
						await _unhandledExceptionHandler(session, message, ex);
					}					
				}

			}, messageHandlerOptions);
		}
	}
}
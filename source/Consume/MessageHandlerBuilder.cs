using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Extensions.DependencyInjection;
using TinyDancer.GracefulShutdown;

namespace TinyDancer.Consume
{
	public class MessageHandlerBuilder
	{
		enum ReceiverMode
		{
			Message, Session
		}

		private readonly ReceiverClientAdapter _receiverClient;
		private readonly SessionConfiguration _sessionConfiguration;
		private readonly Configuration _singleMessageConfiguration;

		private Func<IReceiverClient, Message, Task> _unrecognizedMessageHandler = null;
		private ExceptionHandler _deserializationErrorHandler = null;
		private Action<ExceptionReceivedEventArgs> _receiverActionCallback = null;

		private readonly Dictionary<Type, ExceptionHandler> _exceptionHandlers = new Dictionary<Type, ExceptionHandler>();
		private ExceptionHandler _unhandledExceptionHandler = null;

		private readonly Dictionary<string, (Type type, Func<object, Task> handler)> _messageHandlers = new Dictionary<string, (Type type, Func<object, Task> handler)>();
		private (Type type, Func<object, Task> handler)? _globalHandler = null;

		private bool _anyDependenciesRegistered;
		private IServiceCollection _services;

		private readonly ReceiverMode _mode;
		private bool _setCulture;

		internal MessageHandlerBuilder(ReceiverClientAdapter receiverClient, Configuration singleMessageConfiguration)
		{
			_receiverClient = receiverClient;
			_singleMessageConfiguration = singleMessageConfiguration;

			_mode = ReceiverMode.Message;
		}

		internal MessageHandlerBuilder(ReceiverClientAdapter receiverClient, SessionConfiguration sessionConfiguration)
		{
			_receiverClient = receiverClient;
			_sessionConfiguration = sessionConfiguration;

			_mode = ReceiverMode.Session;
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

		// Asynchronous overloads

		public MessageHandlerBuilder HandleMessage<TMessage>(Func<TMessage, Task> action) => RegisterMessageHandler(action, typeof(TMessage));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1>(Func<TMessage, TDep1, Task> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2>(Func<TMessage, TDep1, TDep2, Task> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3>(Func<TMessage, TDep1, TDep2, TDep3, Task> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, Task> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, Task> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, Task> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, Task> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7));

		// Synchronous overloads

		public MessageHandlerBuilder HandleMessage<TMessage>(Action<TMessage> action) => RegisterMessageHandler(action, typeof(TMessage));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1>(Action<TMessage, TDep1> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2>(Action<TMessage, TDep1, TDep2> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3>(Action<TMessage, TDep1, TDep2, TDep3> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4>(Action<TMessage, TDep1, TDep2, TDep3, TDep4> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7));

		// Global async overloads

		public MessageHandlerBuilder HandleAllAs<TMessage>(Func<TMessage, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1>(Func<TMessage, TDep1, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2>(Func<TMessage, TDep1, TDep2, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3>(Func<TMessage, TDep1, TDep2, TDep3, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7));
		
		// Global overloads

		public MessageHandlerBuilder HandleAllAs<TMessage>(Action<TMessage> handler) => RegisterGlobalHandler(handler, typeof(TMessage));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1>(Action<TMessage, TDep1> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2>(Action<TMessage, TDep1, TDep2> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3>(Action<TMessage, TDep1, TDep2, TDep3> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4>(Action<TMessage, TDep1, TDep2, TDep3, TDep4> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7));

		public MessageHandlerBuilder ConfigureCulture()
		{
			_setCulture = true;
			return this;
		}

		private MessageHandlerBuilder RegisterMessageHandler(Action<(Type type, Func<object, Task> handler)> registerHandler, Delegate action, Type messageType, params Type[] dependencies)
		{
			registerHandler((messageType, async (message) =>
			{
				if (dependencies.Any())
				{
					using (var scope = _services.BuildServiceProvider().CreateScope())
					{
						var resolvedDependencies = dependencies.Select(type =>
							scope.ServiceProvider.GetService(type) ??
							throw new DependencyResolutionException($"Service of type {type.FullName} could not be resolved"));

						var returnedObject = action.DynamicInvoke(new [] { message }.Concat(resolvedDependencies).ToArray());

						await (returnedObject is Task task
							? task
							: Task.CompletedTask);
					}
				}
				else
				{
					await (action.DynamicInvoke(message) is Task task
							? task
							: Task.CompletedTask);
				}
			}));

			_anyDependenciesRegistered |= dependencies.Any();

			return this;
		}

		private MessageHandlerBuilder RegisterMessageHandler(Delegate action, Type messageType, params Type[] dependencies)
		{
			void RegisterHandler((Type type, Func<object, Task> handler) handler) => _messageHandlers[messageType.FullName] = handler;
			RegisterMessageHandler(RegisterHandler, action, messageType, dependencies);

			return this;
		}

		private MessageHandlerBuilder RegisterGlobalHandler(Delegate action, Type messageType, params Type[] dependencies)
		{
			void RegisterHandler((Type type, Func<object, Task> handler) handler) => _globalHandler = handler;
			RegisterMessageHandler(RegisterHandler, action, messageType, dependencies);

			return this;
		}

		public void Subscribe() => DoSubscribe();

		[Obsolete("This overload is deprecated in favor of SubscribeUntilShutdownAsync")]
		public void Subscribe(CancellationToken? cancelled = null, Func<IDisposable> dontInterrupt = null)
			=> DoSubscribe(cancelled, dontInterrupt);

		/// <summary>
		/// Start subscribing and allow outstanding message handlers to finish before completing.
		/// No new messages will be handled once shutdown has begun.
		/// </summary>
		/// <param name="applicationStopping">A cancellation token that is cancelled when shutdown begins</param>
		/// <returns>A task that is completed when all outstanding message handlers are finished</returns>
		public Task SubscribeUntilShutdownAsync(CancellationToken applicationStopping)
		{
			var runContext = new GracefulConsoleRunContext(applicationStopping);
			var applicationShutdown = new TaskCompletionSource<object>();

			applicationStopping.Register(() =>
			{
				runContext.TerminationRequested();
				Task.WaitAll(runContext.WhenWorkCompleted(null));
				applicationShutdown.TrySetResult(null);
			}, false);

			DoSubscribe(runContext.ApplicationTermination, runContext.BlockInterruption);

			return applicationShutdown.Task;
		}

		public MessageHandlerBuilder RegisterDependencies(IServiceCollection services)
		{
			_services = services;
			return this;
		}

		private void DoSubscribe(CancellationToken? cancelled = null, Func<IDisposable> dontInterrupt = null)
		{
			if (_unrecognizedMessageHandler != null && _globalHandler != null)
			{
				throw new InvalidOperationException($"{nameof(HandleAllAs)} and {nameof(OnUnrecognizedMessageType)} cannot both be used");
			}

			if (_services == null && _anyDependenciesRegistered)
			{
				throw new InvalidOperationException($"Please use {nameof(RegisterDependencies)} in order to enable dependency resolution");
			}

			var blockInterruption = dontInterrupt ?? (() => new DummyDisposable());

			if (_mode == ReceiverMode.Message)
			{
				var messageHandlerOptions = new MessageHandlerOptions(exceptionEventArgs =>
				{
					_receiverActionCallback?.Invoke(exceptionEventArgs); // todo: implementera async version också
					return Task.CompletedTask;
				})
				{
					AutoComplete = false,
				};

				if (_singleMessageConfiguration?.MaxConcurrentMessages != null)
				{
					messageHandlerOptions.MaxConcurrentCalls = _singleMessageConfiguration.MaxConcurrentMessages.Value;
				}

				if (_singleMessageConfiguration?.MaxAutoRenewDuration != null)
				{
					messageHandlerOptions.MaxAutoRenewDuration = _singleMessageConfiguration.MaxAutoRenewDuration.Value;
				}

				_receiverClient.RegisterMessageHandler(async (message, cancel) =>
				{
					await HandleMessageReceived(cancelled, message, blockInterruption, _receiverClient.GetClient());
				}, messageHandlerOptions);	
			}
			else
			{
				var messageHandlerOptions = new SessionHandlerOptions(exceptionEventArgs =>
				{
					_receiverActionCallback?.Invoke(exceptionEventArgs); // todo: implementera async version också
					return Task.CompletedTask;
				})
				{
					AutoComplete = false
				};

				if (_sessionConfiguration?.MaxConcurrentSessions != null)
				{
					messageHandlerOptions.MaxConcurrentSessions = _sessionConfiguration.MaxConcurrentSessions.Value;
				}

				if (_sessionConfiguration?.MaxAutoRenewDuration != null)
				{
					messageHandlerOptions.MaxAutoRenewDuration = _sessionConfiguration.MaxAutoRenewDuration.Value;
				}

				_receiverClient.RegisterSessionHandler(async (session, message, cancel) =>
				{
					await HandleMessageReceived(cancelled, message, blockInterruption, session);
				}, messageHandlerOptions);
			}
		}

		private async Task HandleMessageReceived(CancellationToken? cancelled, Message message, Func<IDisposable> blockInterruption, IReceiverClient client)
		{
			if (cancelled?.IsCancellationRequested == true)
			{
				await client.AbandonAsync(message.SystemProperties.LockToken);
				return;
			}

			cancelled?.Register(() => { client.CloseAsync(); });

			using (blockInterruption())
			{
				try
				{
					CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo((string) message.UserProperties["Culture"]);
					
					_services?.AddScoped(provider => message);

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
						await _unrecognizedMessageHandler(client, message);
						return;
					}

					await client.CompleteAsync(message.SystemProperties.LockToken);
				}
				catch (DeserializationFailedException ex) when (_deserializationErrorHandler != null)
				{
					await _deserializationErrorHandler(client, message, ex.InnerException);
				}
				catch (Exception ex) when (_exceptionHandlers.Keys.Contains(ex.GetType()))
				{
					await _exceptionHandlers[ex.GetType()](client, message, ex);
				}
				catch (Exception ex) when (_unhandledExceptionHandler != null)
				{
					await _unhandledExceptionHandler(client, message, ex);
				}
				finally
				{
					CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture;
				}
			}
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
	}

	public class DependencyResolutionException : Exception
	{
		public DependencyResolutionException(string message) : base(message)
		{
			
		}
	}
}
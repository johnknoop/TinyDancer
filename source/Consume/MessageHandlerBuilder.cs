using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Extensions.DependencyInjection;
using TinyDancer.GracefulShutdown;

namespace TinyDancer.Consume
{
	public class MessageHolder
	{
		public Message Message { get; set; }
	}

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
		private ExceptionHandler _dependencyResolutionErrorHandler = null;
		private Action<ExceptionReceivedEventArgs> _receiverExceptionCallback = null;

		private readonly Dictionary<Type, ExceptionHandler> _exceptionHandlers = new Dictionary<Type, ExceptionHandler>();
		private ExceptionHandler _unhandledExceptionHandler = null;

		private readonly Dictionary<string, (Type type, Func<object, IServiceScope, Task> handler)> _messageHandlers = new Dictionary<string, (Type type, Func<object, IServiceScope, Task> handler)>();
		private (Type type, Func<object, IServiceScope, Task> handler)? _globalHandler = null;

		private bool _anyDependenciesRegistered;
		private IServiceProvider _services;

		private readonly ReceiverMode _mode;
		private bool _setCulture;

		internal MessageHandlerBuilder(ReceiverClientAdapter receiverClient, Configuration singleMessageConfiguration)
		{
			_receiverClient = receiverClient;
			_singleMessageConfiguration = singleMessageConfiguration;

			_mode = ReceiverMode.Message;

			// Just so that we can add and resolve the MessageReleaser.
			// If the user overwrites this using RegisterDependencies then that's fine
			// because we're adding it to that service collection as well.
			_services = new ServiceCollection().AddScoped<MessageReleaser>().BuildServiceProvider();
		}

		internal MessageHandlerBuilder(ReceiverClientAdapter receiverClient, SessionConfiguration sessionConfiguration)
		{
			_receiverClient = receiverClient;
			_sessionConfiguration = sessionConfiguration;

			_mode = ReceiverMode.Session;

			// Just so that we can add and resolve the MessageReleaser.
			// If the user overwrites this using RegisterDependencies then that's fine
			// because we're adding it to that service collection as well.
			_services = new ServiceCollection().AddScoped<MessageReleaser>().BuildServiceProvider();
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
			_receiverExceptionCallback = callback;

			return this;
		}

        public MessageHandlerBuilder OnDependencyResolutionException(Func<ExceptionHandlingBuilder<Exception>, ExceptionHandler<Exception>> handleStrategy, ExceptionCallback<Exception> callback = null)
        {
            var handler = handleStrategy(new ExceptionHandlingBuilder<Exception>());

            _dependencyResolutionErrorHandler = async (client, message, ex) =>
            {
                await handler(client, message, ex);
                callback?.Invoke(message, ex);
            };

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
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8, Task> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7), typeof(TDep8));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8, TDep9>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8, TDep9, Task> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7), typeof(TDep8), typeof(TDep9));

		// Synchronous overloads

		public MessageHandlerBuilder HandleMessage<TMessage>(Action<TMessage> action) => RegisterMessageHandler(action, typeof(TMessage));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1>(Action<TMessage, TDep1> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2>(Action<TMessage, TDep1, TDep2> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3>(Action<TMessage, TDep1, TDep2, TDep3> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4>(Action<TMessage, TDep1, TDep2, TDep3, TDep4> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7), typeof(TDep8));
		public MessageHandlerBuilder HandleMessage<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8, TDep9>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8, TDep9> action) => RegisterMessageHandler(action, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7), typeof(TDep8), typeof(TDep9));

		// Global async overloads

		public MessageHandlerBuilder HandleAllAs<TMessage>(Func<TMessage, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1>(Func<TMessage, TDep1, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2>(Func<TMessage, TDep1, TDep2, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3>(Func<TMessage, TDep1, TDep2, TDep3, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7), typeof(TDep8));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8, TDep9>(Func<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8, TDep9, Task> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7), typeof(TDep8), typeof(TDep9));
		
		// Global overloads

		public MessageHandlerBuilder HandleAllAs<TMessage>(Action<TMessage> handler) => RegisterGlobalHandler(handler, typeof(TMessage));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1>(Action<TMessage, TDep1> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2>(Action<TMessage, TDep1, TDep2> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3>(Action<TMessage, TDep1, TDep2, TDep3> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4>(Action<TMessage, TDep1, TDep2, TDep3, TDep4> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7), typeof(TDep8));
		public MessageHandlerBuilder HandleAllAs<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8, TDep9>(Action<TMessage, TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8, TDep9> handler) => RegisterGlobalHandler(handler, typeof(TMessage), typeof(TDep1), typeof(TDep2), typeof(TDep3), typeof(TDep4), typeof(TDep5), typeof(TDep6), typeof(TDep7), typeof(TDep8), typeof(TDep9));

		[Obsolete("Use " + nameof(ConsumeMessagesInSameCultureAsSentIn) + " instead")]
		public MessageHandlerBuilder ConfigureCulture()
		{
			_setCulture = true;
			return this;
		}

		public MessageHandlerBuilder ConsumeMessagesInSameCultureAsSentIn()
		{
			_setCulture = true;
			return this;
		}

		private MessageHandlerBuilder RegisterMessageHandler(Action<(Type type, Func<object, IServiceScope, Task> handler)> registerHandler, Delegate action, Type messageType, params Type[] dependencies)
		{
			registerHandler((messageType, async (message, scope) =>
			{
				if (dependencies.Any())
				{
					var resolvedDependencies = dependencies.Select(type =>
						scope.ServiceProvider.GetService(type) ??
								throw new DependencyResolutionException($"Service of type {type.FullName} could not be resolved")
						);

					var returnedObject = action.DynamicInvoke(new [] { message }.Concat(resolvedDependencies).ToArray());

					await (returnedObject is Task task
						? task
						: Task.CompletedTask);
				}
				else
				{
					await (action.DynamicInvoke(message) is Task task
							? task
							: Task.CompletedTask);
				}
			}));

			_anyDependenciesRegistered |= dependencies.Where(x => x != typeof(MessageReleaser)).Any();

			return this;
		}

		private MessageHandlerBuilder RegisterMessageHandler(Delegate action, Type messageType, params Type[] dependencies)
		{
			void RegisterHandler((Type type, Func<object, IServiceScope, Task> handler) handler) => _messageHandlers[messageType.FullName] = handler;
			RegisterMessageHandler(RegisterHandler, action, messageType, dependencies);

			return this;
		}

		private MessageHandlerBuilder RegisterGlobalHandler(Delegate action, Type messageType, params Type[] dependencies)
		{
			void RegisterHandler((Type type, Func<object, IServiceScope, Task> handler) handler) => _globalHandler = handler;
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
			services.AddScoped<Message>(x => x.GetRequiredService<MessageHolder>().Message);
			services.AddScoped<MessageHolder>();
			services.AddScoped<MessageReleaser>();

			_services = services.BuildServiceProvider();

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
                var messageHandlerOptions = new MessageHandlerOptions(
                    exceptionReceivedHandler: exceptionEventArgs =>
                    {
                        _receiverExceptionCallback
                            ?.Invoke(exceptionEventArgs); // todo: implementera async version också
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
                var messageHandlerOptions = new SessionHandlerOptions(
                    exceptionReceivedHandler: exceptionEventArgs =>
                    {
                        _receiverExceptionCallback
                            ?.Invoke(exceptionEventArgs); // todo: implementera async version också
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
			(bool cultureOverridden, CultureInfo defaultCulture, CultureInfo defaultUiCulture) TryOverrideCulture()
			{
				var cultureOverridden = false;
				var defaultCulture = CultureInfo.CurrentCulture;
				var defaultUiCulture = CultureInfo.CurrentUICulture;

				if (_setCulture && message.UserProperties.ContainsKey("Culture") && message.UserProperties["Culture"] != null)
				{
					var culture = CultureInfo.GetCultureInfo((string) message.UserProperties["Culture"]);

					CultureInfo.CurrentCulture = culture;
					CultureInfo.CurrentUICulture = culture;

					cultureOverridden = true;
				}

				return (cultureOverridden, defaultCulture, defaultUiCulture);
			}

			if (cancelled?.IsCancellationRequested == true)
			{
				await client.AbandonAsync(message.SystemProperties.LockToken);
				return;
			}

			cancelled?.Register(() => { client.CloseAsync(); });

			using (blockInterruption())
			{
				var (cultureOverridden, defaultCulture, defaultUiCulture) = TryOverrideCulture();

				try
				{
					using (var scope = _services?.CreateScope())
					{
						var messageReleased = false;

						if (scope != null)
						{
							if (scope.ServiceProvider.TryGetService<MessageHolder>(out var messageHolder))
							{
								messageHolder.Message = message;
							}

							var messageReleaser = scope.ServiceProvider.GetRequiredService<MessageReleaser>();

							messageReleaser
								.OnAbandon(async () =>
								{
									await client.AbandonAsync(message.SystemProperties.LockToken);
									messageReleased = true;
								})
								.OnComplete(async () =>
								{
									await client.CompleteAsync(message.SystemProperties.LockToken);
									messageReleased = true;
								})
								.OnDeadletter(async () =>
								{
									await client.DeadLetterAsync(message.SystemProperties.LockToken);
									messageReleased = true;
								});
						}

						if (message.UserProperties.TryGetValue("MessageType", out var messageType) &&
							_messageHandlers.TryGetValue((string) messageType, out var y))
						{
							var deserialized = Deserialize(message, y.type);
							await y.handler(deserialized, scope);
						}
						else if (_globalHandler != null)
						{
							var deserialized = Deserialize(message, _globalHandler.Value.type);
							await _globalHandler.Value.handler(deserialized, scope);
						}
						else if (_unrecognizedMessageHandler != null)
						{
							await _unrecognizedMessageHandler(client, message);
							return;
						}

						if (!messageReleased)
						{
							await client.CompleteAsync(message.SystemProperties.LockToken);
						}
					}
				}
				catch (DeserializationFailedException ex) when (_deserializationErrorHandler != null)
				{
					await _deserializationErrorHandler(client, message, ex.InnerException);
				}
				catch (DependencyResolutionException ex) when (_dependencyResolutionErrorHandler != null)
                {
                    await _dependencyResolutionErrorHandler(client, message, ex.InnerException);
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
					if (cultureOverridden)
					{
						CultureInfo.CurrentCulture = defaultCulture;
						CultureInfo.CurrentUICulture = defaultUiCulture;
					}
				}
			}
		}

		object Deserialize(Message message, Type type)
		{
            return message.Body.Deserialize(type);
		}
	}

	public class DependencyResolutionException : Exception
	{
		public DependencyResolutionException(string message) : base(message)
		{
			
		}
	}
}
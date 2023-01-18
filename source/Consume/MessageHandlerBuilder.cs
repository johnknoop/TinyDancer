using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using TinyDancer.GracefulShutdown;

namespace TinyDancer.Consume
{
	public class MessageHolder
	{
		public ServiceBusReceivedMessage Message { get; set; }
	}

	internal delegate Task DeadLetterMessageAsync(
		ServiceBusReceivedMessage message,
		string deadLetterReason,
		string deadLetterErrorDescription = default,
		CancellationToken cancellationToken = default
	);

	internal delegate Task InternalExceptionHandler(ProcessMessageEventArgs? message, ProcessSessionMessageEventArgs? sessionMessage, Exception exception);

	public class MessageHandlerBuilder
	{
		enum ReceiverMode
		{
			Message, Session
		}

		private readonly ServiceBusProcessor? _messageProcessor;
		private readonly ServiceBusSessionProcessor? _sessionProcessor;

		private Func<ProcessMessageEventArgs?, ProcessSessionMessageEventArgs?, Task> _unrecognizedMessageHandler = null;
		private InternalExceptionHandler _deserializationErrorHandler = null;
		private InternalExceptionHandler _dependencyResolutionErrorHandler = null;
		private Action<ProcessErrorEventArgs> _receiverExceptionCallback = null;

		private readonly Dictionary<Type, InternalExceptionHandler> _exceptionHandlers = new();
		private InternalExceptionHandler _unhandledExceptionHandler = null;

		private readonly Dictionary<string, (Type type, Func<object, IServiceScope?, Task> handler)> _messageHandlers = new();
		private (Type type, Func<object, IServiceScope?, Task> handler)? _globalHandler = null;

		private bool _anyDependenciesRegistered;
		private IServiceProvider _services;

		private readonly ReceiverMode _mode;

		[MemberNotNullWhen(true, nameof(_sessionProcessor))]
		[MemberNotNullWhen(false, nameof(_messageProcessor))]
		private bool InSessionMode => _mode == ReceiverMode.Session;

		private bool _setCulture;
		private bool _blockInterruption;
		private TimeSpan _blockInterruptionTimeout;

		internal MessageHandlerBuilder(ServiceBusProcessor processor)
		{
			_mode = ReceiverMode.Message;

			// Just so that we can add and resolve the MessageSettler.
			// If the user overwrites this using RegisterDependencies then that's fine
			// because we're adding it to that service collection as well.
			_services = new ServiceCollection().AddScoped<MessageSettler>().BuildServiceProvider();
			_messageProcessor = processor;
		}

		internal MessageHandlerBuilder(ServiceBusSessionProcessor sessionProcessor)
		{
			_mode = ReceiverMode.Session;

			// Just so that we can add and resolve the MessageSettler.
			// If the user overwrites this using RegisterDependencies then that's fine
			// because we're adding it to that service collection as well.
			_services = new ServiceCollection().AddScoped<MessageSettler>().BuildServiceProvider();
			_sessionProcessor = sessionProcessor;
		}

		public MessageHandlerBuilder Catch<TException>(Func<ExceptionHandlingBuilder<TException>, ExceptionHandler<TException>> handleStrategy, ExceptionCallback<TException> callback = null) where TException : Exception
		{
			_exceptionHandlers[typeof(TException)] = async (message, sessionMessage, ex) =>
			{
				var handler = handleStrategy(new ExceptionHandlingBuilder<TException>(
					deadletterDelegate: message != null
						? message.DeadLetterMessageAsync
						: sessionMessage!.DeadLetterMessageAsync,
					completeMessageAsync: async msg => await (message?.CompleteMessageAsync(message.Message) ?? sessionMessage!.CompleteMessageAsync(sessionMessage.Message)),
					abandonMessageAsync: async msg => await (message?.AbandonMessageAsync(message.Message) ?? sessionMessage!.AbandonMessageAsync(sessionMessage.Message))
				));

				await handler(message?.Message ?? sessionMessage!.Message, (TException)ex);
				callback?.Invoke(message?.Message ?? sessionMessage!.Message, (TException)ex);
			};

			return this;
		}

		public MessageHandlerBuilder CatchUnhandledExceptions(Func<ExceptionHandlingBuilder<Exception>, ExceptionHandler<Exception>> handleStrategy, ExceptionCallback<Exception> callback = null)
		{
			_unhandledExceptionHandler = async (message, sessionMessage, ex) =>
			{
				var handler = handleStrategy(new ExceptionHandlingBuilder<Exception>(
					deadletterDelegate: message != null
						? message.DeadLetterMessageAsync
						: sessionMessage!.DeadLetterMessageAsync,
					completeMessageAsync: async msg => await (message?.CompleteMessageAsync(message.Message) ?? sessionMessage!.CompleteMessageAsync(sessionMessage.Message)),
					abandonMessageAsync: async msg => await (message?.AbandonMessageAsync(message.Message) ?? sessionMessage!.AbandonMessageAsync(sessionMessage.Message))
				));

				await handler(message?.Message ?? sessionMessage!.Message, ex);
				callback?.Invoke(message?.Message ?? sessionMessage!.Message, ex);
			};

			return this;
		}

		public MessageHandlerBuilder OnDeserializationFailed(Func<ExceptionHandlingBuilder<Exception>, ExceptionHandler<Exception>> handleStrategy, ExceptionCallback<Exception> callback = null)
		{
			_deserializationErrorHandler = async (message, sessionMessage, ex) =>
			{
				var handler = handleStrategy(new ExceptionHandlingBuilder<Exception>(
					deadletterDelegate: message != null
						? message.DeadLetterMessageAsync
						: sessionMessage!.DeadLetterMessageAsync,
					completeMessageAsync: async msg => await (message?.CompleteMessageAsync(message.Message) ?? sessionMessage!.CompleteMessageAsync(sessionMessage.Message)),
					abandonMessageAsync: async msg => await (message?.AbandonMessageAsync(message.Message) ?? sessionMessage!.AbandonMessageAsync(sessionMessage.Message))
				));

				await handler(message?.Message ?? sessionMessage!.Message, ex);
				callback?.Invoke(message?.Message ?? sessionMessage!.Message, ex);
			};

			return this;
		}

		public MessageHandlerBuilder OnUnrecognizedMessageType(Func<UnrecognizedMessageHandlingBuilder, UnrecognizedMessageHandler> handleStrategy, Action<ServiceBusReceivedMessage> callback = null)
		{
			_unrecognizedMessageHandler = async (message, sessionMessage) =>
			{
				var handler = handleStrategy(new UnrecognizedMessageHandlingBuilder(
					deadletterDelegate: message != null
						? message.DeadLetterMessageAsync
						: sessionMessage!.DeadLetterMessageAsync,
					completeMessageAsync: async msg => await (message?.CompleteMessageAsync(message.Message) ?? sessionMessage!.CompleteMessageAsync(sessionMessage.Message)),
					abandonMessageAsync: async msg => await (message?.AbandonMessageAsync(message.Message) ?? sessionMessage!.AbandonMessageAsync(sessionMessage.Message))
				));

				await handler(message?.Message ?? sessionMessage!.Message);
				callback?.Invoke(message?.Message ?? sessionMessage!.Message);
			};

			return this;
		}

		public MessageHandlerBuilder OnReceiverException(Action<ProcessErrorEventArgs> callback)
		{
			_receiverExceptionCallback = callback;

			return this;
		}

		public MessageHandlerBuilder OnDependencyResolutionException(Func<ExceptionHandlingBuilder<Exception>, ExceptionHandler<Exception>> handleStrategy, ExceptionCallback<Exception> callback = null)
		{			_dependencyResolutionErrorHandler = async (message, sessionMessage, ex) =>
			{
				var handler = handleStrategy(new ExceptionHandlingBuilder<Exception>(
					deadletterDelegate: message != null
						? message.DeadLetterMessageAsync
						: sessionMessage!.DeadLetterMessageAsync,
					completeMessageAsync: async msg => await (message?.CompleteMessageAsync(message.Message) ?? sessionMessage!.CompleteMessageAsync(sessionMessage.Message)),
					abandonMessageAsync: async msg => await (message?.AbandonMessageAsync(message.Message) ?? sessionMessage!.AbandonMessageAsync(sessionMessage.Message))
				));

				await handler(message?.Message ?? sessionMessage!.Message, ex);
				callback?.Invoke(message?.Message ?? sessionMessage!.Message, ex);
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

		private MessageHandlerBuilder RegisterMessageHandler(Action<(Type type, Func<object, IServiceScope?, Task> handler)> registerHandler, Delegate action, Type messageType, params Type[] dependencies)
		{
			registerHandler((messageType, async (message, scope) =>
			{
				if (dependencies.Any())
				{
					var resolvedDependencies = dependencies.Select(type =>
						scope.ServiceProvider.GetService(type) ??
								throw new DependencyResolutionException($"Service of type {type.FullName} could not be resolved")
						);

					var returnedObject = action.DynamicInvoke(new[] { message }.Concat(resolvedDependencies).ToArray());

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
			}
			));

			_anyDependenciesRegistered |= dependencies.Where(x => x != typeof(MessageSettler)).Any();

			return this;
		}

		private MessageHandlerBuilder RegisterMessageHandler(Delegate action, Type messageType, params Type[] dependencies)
		{
			void RegisterHandler((Type type, Func<object, IServiceScope?, Task> handler) handler) => _messageHandlers[messageType.FullName] = handler;
			RegisterMessageHandler(RegisterHandler, action, messageType, dependencies);

			return this;
		}

		private MessageHandlerBuilder RegisterGlobalHandler(Delegate action, Type messageType, params Type[] dependencies)
		{
			void RegisterHandler((Type type, Func<object, IServiceScope?, Task> handler) handler) => _globalHandler = handler;
			RegisterMessageHandler(RegisterHandler, action, messageType, dependencies);

			return this;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="blockInterruption">Pass <c>true</c> to make sure ongoing message processing is drained before allowing the application to exit. This is useful if your message handling code for example has multiple side-effects, such as multiple database writes without a transaction.</param>
		/// <param name="blockInterruptionTimeout">The maximum duration to block interruption</param>
		/// <param name="cancellationToken">A cancellation token, typically representing application shutdown</param>
		/// <returns></returns>
		public Task SubscribeAsync(bool blockInterruption, TimeSpan blockInterruptionTimeout, CancellationToken cancellationToken) =>
			DoSubscribe(blockInterruption, blockInterruptionTimeout, cancellationToken);

		/// <summary>
		/// 
		/// </summary>
		/// <param name="blockInterruption">Pass <c>true</c> to make sure ongoing message processing is drained before allowing the application to exit. This is useful if your message handling code for example has multiple side-effects, such as multiple database writes without a transaction.</param>
		/// <param name="cancellationToken">A cancellation token, typically representing application shutdown</param>
		/// <returns></returns>
		public Task SubscribeAsync(bool blockInterruption, CancellationToken cancellationToken) =>
			DoSubscribe(blockInterruption, TimeSpan.FromMilliseconds(-1), cancellationToken);

		/// <summary>
		/// 
		/// </summary>
		/// <param name="cancellationToken">A cancellation token, typically representing application shutdown</param>
		/// <returns></returns>
		public Task SubscribeAsync(CancellationToken cancellationToken) =>
			DoSubscribe(false, TimeSpan.Zero, cancellationToken);

		public Task SubscribeAsync() =>
			DoSubscribe(false, TimeSpan.Zero, CancellationToken.None);

		public MessageHandlerBuilder RegisterDependencies(IServiceCollection services)
		{
			services.AddScoped<ServiceBusReceivedMessage>(x => x.GetRequiredService<MessageHolder>().Message);
			services.AddScoped<MessageHolder>();
			services.AddScoped<MessageSettler>();

			_services = services.BuildServiceProvider();

			return this;
		}

		private async Task DoSubscribe(bool blockInterruption, TimeSpan blockInterruptionTimeout, CancellationToken cancel)
		{
			if (_unrecognizedMessageHandler != null && _globalHandler != null)
			{
				throw new InvalidOperationException($"{nameof(HandleAllAs)} and {nameof(OnUnrecognizedMessageType)} cannot both be used");
			}

			if (_services == null && _anyDependenciesRegistered)
			{
				throw new InvalidOperationException($"Please use {nameof(RegisterDependencies)} in order to enable dependency resolution");
			}

			_blockInterruption = blockInterruption;
			_blockInterruptionTimeout = blockInterruptionTimeout;

			if (InSessionMode)
			{
				_sessionProcessor.ProcessMessageAsync += async (args) =>
				{
					await HandleMessageReceived(
						messageArgs: null,
						sessionMessageArgs: args,
						completeMessageAsync: (msg) => args.CompleteMessageAsync(msg),
						deadLetterMessageAsync: args.DeadLetterMessageAsync,
						abandonMessageAsync: (msg) => args.AbandonMessageAsync(msg),
						cancel: cancel
					);
				};

				_sessionProcessor.ProcessErrorAsync += (args) =>
				{
					_receiverExceptionCallback?.Invoke(args); // todo: implementera async version också
					return Task.CompletedTask;
				};

				await _sessionProcessor.StartProcessingAsync(cancel);

				cancel.Register(() =>
				{
					var closeTask = _sessionProcessor.CloseAsync();

					if (_blockInterruption)
					{
						closeTask.Wait(_blockInterruptionTimeout);
					}
				});
			}
			else
			{
				_messageProcessor.ProcessMessageAsync += async (args) =>
				{
					await HandleMessageReceived(
						messageArgs: args,
						sessionMessageArgs: null,
						completeMessageAsync: (msg) => args.CompleteMessageAsync(msg),
						deadLetterMessageAsync: args.DeadLetterMessageAsync,
						abandonMessageAsync: (msg) => args.AbandonMessageAsync(msg),
						cancel: cancel
					);
				};

				_messageProcessor.ProcessErrorAsync += (args) =>
				{
					_receiverExceptionCallback?.Invoke(args); // todo: implementera async version också
					return Task.CompletedTask;
				};

				await _messageProcessor.StartProcessingAsync(cancel);

				cancel.Register(() =>
				{
					var closeTask = _messageProcessor.CloseAsync();

					if (_blockInterruption)
					{
						closeTask.Wait(_blockInterruptionTimeout);
					}
				});
			}
		}

		private async Task HandleMessageReceived(
			ProcessMessageEventArgs? messageArgs,
			ProcessSessionMessageEventArgs? sessionMessageArgs,
			Func<ServiceBusReceivedMessage, Task> completeMessageAsync,
			DeadLetterMessageAsync deadLetterMessageAsync,
			Func<ServiceBusReceivedMessage, Task> abandonMessageAsync,
			CancellationToken cancel)
		{
			var message = messageArgs?.Message ?? sessionMessageArgs!.Message;

			(bool cultureOverridden, CultureInfo defaultCulture, CultureInfo defaultUiCulture) TryOverrideCulture()
			{
				var cultureOverridden = false;
				var defaultCulture = CultureInfo.CurrentCulture;
				var defaultUiCulture = CultureInfo.CurrentUICulture;

				if (_setCulture && message.ApplicationProperties.ContainsKey("Culture") && message.ApplicationProperties["Culture"] != null)
				{
					var culture = CultureInfo.GetCultureInfo((string)message.ApplicationProperties["Culture"]);

					CultureInfo.CurrentCulture = culture;
					CultureInfo.CurrentUICulture = culture;

					cultureOverridden = true;
				}

				return (cultureOverridden, defaultCulture, defaultUiCulture);
			}

			if (cancel.IsCancellationRequested)
			{
				await abandonMessageAsync(message);
				return;
			}

			var (cultureOverridden, defaultCulture, defaultUiCulture) = TryOverrideCulture();

			try
			{
				using (var scope = _services?.CreateScope())
				{
					var messageSettled = false;

					if (scope != null)
					{
						if (scope.ServiceProvider.TryGetService<MessageHolder>(out var messageHolder))
						{
							messageHolder.Message = message;
						}

						var messageSettler = scope.ServiceProvider.GetRequiredService<MessageSettler>();

						messageSettler
							.OnAbandon(async () =>
							{
								await abandonMessageAsync(message);
								messageSettled = true;
							})
							.OnComplete(async () =>
							{
								if ((_messageProcessor?.AutoCompleteMessages??_sessionProcessor!.AutoCompleteMessages) == false)
								{
									await completeMessageAsync(message);
								}

								messageSettled = true;
							})
							.OnDeadletter(async (reason) =>
							{
								await deadLetterMessageAsync(message, reason ?? string.Empty);
								messageSettled = true;
							});
					}

					if (message.ApplicationProperties.TryGetValue("MessageType", out var messageType) &&
						_messageHandlers.TryGetValue((string)messageType, out var y))
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
						await _unrecognizedMessageHandler(messageArgs, sessionMessageArgs);
						return;
					}

					if (!messageSettled)
					{
						await completeMessageAsync(message);
					}
				}
			}
			catch (DeserializationFailedException ex) when (_deserializationErrorHandler != null)
			{
				await _deserializationErrorHandler(messageArgs, sessionMessageArgs, ex.InnerException!);
			}
			catch (DependencyResolutionException ex) when (_dependencyResolutionErrorHandler != null)
			{
				await _dependencyResolutionErrorHandler(messageArgs, sessionMessageArgs, ex.InnerException!);
			}
			catch (Exception ex) when (_exceptionHandlers.Keys.Contains(ex.GetType()))
			{
				await _exceptionHandlers[ex.GetType()](messageArgs, sessionMessageArgs, ex);
			}
			catch (Exception ex) when (_unhandledExceptionHandler != null)
			{
				await _unhandledExceptionHandler(messageArgs, sessionMessageArgs, ex);
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

		object Deserialize(ServiceBusReceivedMessage message, Type type)
		{
			return message.Body.DeserializeAsync(type);
		}
	}

	public class DependencyResolutionException : Exception
	{
		public DependencyResolutionException(string message) : base(message)
		{

		}
	}
}

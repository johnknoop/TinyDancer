namespace Zwiftly.SharedLibrary.ServiceBusExtensions.Consume
{
	public delegate void MessageHandler<in TMessage>(TMessage message);
}
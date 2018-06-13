namespace TinyDancer.Consume
{
	public delegate void MessageHandler<in TMessage>(TMessage message);
}
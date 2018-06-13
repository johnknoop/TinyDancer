namespace TinyDancer.Consume.Session
{
	public delegate void SessionMessageHandler<in TMessage>(TMessage message, string sessionId);
}
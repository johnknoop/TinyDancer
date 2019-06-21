using System;
using System.Threading.Tasks;

namespace TinyDancer.GracefulShutdown
{
	internal class WorkWrapper : IDisposable
	{
		private readonly TaskCompletionSource<object> _taskCompletionSource;

		internal WorkWrapper(bool succeeded)
		{
			_taskCompletionSource = new TaskCompletionSource<object>();
			Work = _taskCompletionSource.Task;
			Succeeded = succeeded;
		}

		internal Task Work { get; }

		public bool Succeeded { get; private set; }

		public void Dispose()
		{
			_taskCompletionSource.SetResult(null);
		}
	}
}
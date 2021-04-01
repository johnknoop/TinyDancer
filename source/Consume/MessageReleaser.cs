using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TinyDancer.Consume
{
    public class MessageReleaser
    {
        private Func<Task> _doAbandon;
        private Func<Task> _doComplete;
        private Func<Task> _doDeadletter;

        internal MessageReleaser OnAbandon(Func<Task> handler)
		{
            _doAbandon = handler;
            return this;
		}

        internal MessageReleaser OnComplete(Func<Task> handler)
        {
            _doComplete = handler;
            return this;
        }

        internal MessageReleaser OnDeadletter(Func<Task> handler)
        {
            _doDeadletter = handler;
            return this;
        }

        public async Task CompleteAsync()
		{
            await _doComplete().ConfigureAwait(false);
        }

        public async Task AbandonAsync()
        {
            await _doAbandon().ConfigureAwait(false);
        }

        public async Task DeadletterAsync()
        {
            await _doDeadletter().ConfigureAwait(false);
        }
    }
}

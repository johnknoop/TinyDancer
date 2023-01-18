using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TinyDancer.Consume
{
    public class MessageSettler
    {
        private Func<Task> _doAbandon;
        private Func<Task> _doComplete;
        private Func<string?, Task> _doDeadletter;

        internal MessageSettler OnAbandon(Func<Task> handler)
		{
            _doAbandon = handler;
            return this;
		}

        internal MessageSettler OnComplete(Func<Task> handler)
        {
            _doComplete = handler;
            return this;
        }

        internal MessageSettler OnDeadletter(Func<string?, Task> handler)
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

        public async Task DeadletterAsync(string? reason = default)
        {
            await _doDeadletter(reason).ConfigureAwait(false);
        }
    }
}

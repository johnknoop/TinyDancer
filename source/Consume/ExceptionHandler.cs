﻿using System;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;

namespace TinyDancer.Consume
{
	public delegate Task ExceptionHandler(IReceiverClient client, Message message, Exception exception);
}
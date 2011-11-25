﻿namespace NanoMessageBus.RabbitChannel
{
	using System.Collections;
	using RabbitMQ.Client.Events;
	using RabbitMQ.Client.Framing.v0_9;

	public static class ExtensionMethods
	{
		public static int GetAttemptCount(this BasicDeliverEventArgs message)
		{
			message.EnsureMessage();

			if (message.BasicProperties.Headers.Contains(AttemptCountHeader))
				return (int)message.BasicProperties.Headers[AttemptCountHeader];

			return 0;
		}
		public static void SetAttemptCount(this BasicDeliverEventArgs message, int count)
		{
			message.EnsureMessage();

			if (count == 0)
				message.BasicProperties.Headers.Remove(AttemptCountHeader);
			else
				message.BasicProperties.Headers[AttemptCountHeader] = count;
		}
		private static void EnsureMessage(this BasicDeliverEventArgs message)
		{
			message.BasicProperties = message.BasicProperties ?? new BasicProperties();
			message.BasicProperties.Headers = message.BasicProperties.Headers ?? new Hashtable();
		}

		private const string AttemptCountHeader = "x-retry-count";
	}
}
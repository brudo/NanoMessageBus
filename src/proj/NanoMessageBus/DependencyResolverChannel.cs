﻿namespace NanoMessageBus
{
	using System;

	public class DependencyResolverChannel : IMessagingChannel
	{
		public virtual ChannelMessage CurrentMessage
		{
			get { return this.CurrentContext.CurrentMessage; }
		}
		public virtual IChannelTransaction CurrentTransaction
		{
			get { return this.CurrentContext.CurrentTransaction; }
		}
		public virtual IDependencyResolver CurrentResolver
		{
			get { return this.currentResolver ?? this.resolver; }
		}
		protected virtual IDeliveryContext CurrentContext
		{
			get { return this.currentContext ?? this.channel; }
		}

		public virtual void Send(ChannelEnvelope envelope)
		{
			this.CurrentContext.Send(envelope);
		}
		public virtual void BeginShutdown()
		{
			this.channel.BeginShutdown();
		}
		public virtual void Receive(Action<IDeliveryContext> callback)
		{
			this.channel.Receive(context => this.Receive(context, callback));
		}
		protected virtual void Receive(IDeliveryContext context, Action<IDeliveryContext> callback)
		{
			try
			{
				this.currentContext = context;
				this.currentResolver = this.resolver.CreateNestedResolver();
				callback(this);
			}
			finally
			{
				this.currentResolver.Dispose();
				this.currentResolver = null;
				this.currentContext = null;
			}
		}

		public DependencyResolverChannel(IMessagingChannel channel, IDependencyResolver resolver)
		{
			if (channel == null)
				throw new ArgumentNullException("channel");

			if (resolver == null)
				throw new ArgumentNullException("resolver");

			this.channel = channel;
			this.resolver = resolver;
		}
		~DependencyResolverChannel()
		{
			this.Dispose(false);
		}

		public void Dispose()
		{
			this.Dispose(true);
			GC.SuppressFinalize(this);
		}
		protected virtual void Dispose(bool disposing)
		{
			if (!disposing)
				return;

			this.channel.Dispose();
			this.resolver.Dispose();
		}

		private readonly IMessagingChannel channel;
		private readonly IDependencyResolver resolver;
		private IDependencyResolver currentResolver;
		private IDeliveryContext currentContext;
	}
}
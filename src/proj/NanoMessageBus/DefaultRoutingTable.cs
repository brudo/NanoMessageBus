﻿namespace NanoMessageBus
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	public class DefaultRoutingTable : IRoutingTable
	{
		public virtual void Add<T>(IMessageHandler<T> handler, int sequence = int.MaxValue)
		{
			if (handler == null)
				throw new ArgumentNullException("handler");

			this.Add<T>(new SimpleHandler<T>(handler, sequence), handler.GetType());
		}
		public virtual void Add<T>(Func<IHandlerContext, IMessageHandler<T>> callback, int sequence = int.MaxValue, Type handlerType = null)
		{
			if (callback == null)
				throw new ArgumentNullException("callback");

			this.Add<T>(new CallbackHandler<T>(callback, sequence, handlerType), handlerType);
		}
		private void Add<T>(ISequencedHandler handler, Type handlerType)
		{
			IList<ISequencedHandler> routes;
			if (!this.registeredRoutes.TryGetValue(typeof(T), out routes))
				routes = new List<ISequencedHandler>();

			if (handlerType != null && this.registeredHandlers.Contains(handlerType))
			{
				for (var i = 0; i < routes.Count; i++)
				{
					if (handlerType != routes[i].HandlerType)
						continue;

					routes[i] = handler;
					break;
				}
			}
			else
			{
				routes.Add(handler);
				if (handlerType != null)
					this.registeredHandlers.Add(handlerType);
			}

			// TODO: add test to ensure routes are always resorted--even when duplicates are added which might have a different sequence
			this.registeredRoutes[typeof(T)] = routes.OrderBy(x => x.Sequence).ToList();
		}

		public virtual void Route(IHandlerContext context, object message)
		{
			if (context == null)
				throw new ArgumentNullException("context");

			if (message == null)
				throw new ArgumentNullException("message");

			IList<ISequencedHandler> routes;
			if (!this.registeredRoutes.TryGetValue(message.GetType(), out routes))
				return;

			// TODO: extract base class and interface information and perform those routes as well--all the way back to System.Object
			foreach (var route in routes.TakeWhile(x => context.ContinueHandling))
				route.Handle(context, message);
		}

		private readonly ICollection<Type> registeredHandlers = new HashSet<Type>();
		private readonly IDictionary<Type, IList<ISequencedHandler>> registeredRoutes =
			new Dictionary<Type, IList<ISequencedHandler>>();

		private interface ISequencedHandler
		{
			int Sequence { get; }
			Type HandlerType { get; }
			void Handle(IHandlerContext context, object message);
		}
		private class SimpleHandler<T> : ISequencedHandler
		{
			public int Sequence { get; private set; }
			public Type HandlerType { get; private set; }
			public void Handle(IHandlerContext context, object message)
			{
				this.handler.Handle((T)message);
			}
			public SimpleHandler(IMessageHandler<T> handler, int sequence)
			{
				this.handler = handler;
				this.HandlerType = handler.GetType();
				this.Sequence = sequence;
			}
			private readonly IMessageHandler<T> handler;
		}
		private class CallbackHandler<T> : ISequencedHandler
		{
			public int Sequence { get; private set; }
			public Type HandlerType { get; private set; }
			public void Handle(IHandlerContext context, object message)
			{
				var handler = this.callback(context);
				if (handler != null)
					handler.Handle((T)message);
			}
			public CallbackHandler(Func<IHandlerContext, IMessageHandler<T>> callback, int sequence, Type handlerType)
			{
				this.callback = callback;
				this.HandlerType = handlerType;
				this.Sequence = sequence;
			}
			private readonly Func<IHandlerContext, IMessageHandler<T>> callback;
		}
	}
}
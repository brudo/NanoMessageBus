﻿namespace NanoMessageBus.RabbitChannel
{
	using System;
	using System.Collections.Generic;

	public class RabbitTransaction : IChannelTransaction
	{
		public virtual bool Finished
		{
			get { return this.committed || this.rolledBack || this.disposed; }
		}

		public virtual void Register(Action callback)
		{
			if (callback == null)
				throw new ArgumentNullException("callback");

			this.ThrowWhenDisposed();
			this.ThrowWhenRolledBack();
			this.ThrowWhenCommitted();

			this.callbacks.Add(callback);
		}

		public virtual void Commit()
		{
			this.ThrowWhenDisposed();
			this.ThrowWhenRolledBack();

			this.committed = true;

			try
			{
				this.TryCommit();
			}
			finally
			{
				this.callbacks.Clear();
			}
		}
		protected virtual void TryCommit()
		{
			foreach (var callback in this.callbacks)
				callback();

			this.AcknowledgeReceipt();
			this.CommitChannel();
		}
		protected virtual void AcknowledgeReceipt()
		{
			if (this.transactionType != RabbitTransactionType.None)
				this.channel.AcknowledgeMessage();
		}
		protected virtual void CommitChannel()
		{
			if (this.transactionType == RabbitTransactionType.Full)
				this.channel.CommitTransaction();
		}

		public virtual void Rollback()
		{
			if (this.rolledBack)
				return;

			this.ThrowWhenCommitted();
			this.ThrowWhenDisposed();

			this.rolledBack = true; 

			try
			{
				this.RollbackChannel();
			}
			finally
			{
				this.callbacks.Clear();
			}
		}
		protected virtual void RollbackChannel()
		{
			if (this.transactionType == RabbitTransactionType.Full)
				this.channel.RollbackTransaction();
		}

		protected virtual void ThrowWhenDisposed()
		{
			if (this.disposed)
				throw new ObjectDisposedException("RabbitTransaction");
		}
		protected virtual void ThrowWhenRolledBack()
		{
			if (this.rolledBack)
				throw new InvalidOperationException("Cannot perform this operation on a rolled-back transaction.");
		}
		protected virtual void ThrowWhenCommitted()
		{
			if (this.committed)
				throw new InvalidOperationException("The transaction has already committed.");
		}

		public RabbitTransaction(RabbitChannel channel, RabbitTransactionType transactionType)
		{
			this.channel = channel;
			this.transactionType = transactionType;
		}
		~RabbitTransaction()
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
			if (!disposing || this.disposed)
				return;

			this.disposed = true;
			this.callbacks.Clear();

			if (this.committed || this.rolledBack)
				return;

			try
			{
				this.RollbackChannel();
			}
			catch (ChannelConnectionException)
			{
			}
		}

		private readonly ICollection<Action> callbacks = new LinkedList<Action>();
		private readonly RabbitChannel channel;
		private readonly RabbitTransactionType transactionType;
		private bool disposed;
		private bool committed;
		private bool rolledBack;
	}
}
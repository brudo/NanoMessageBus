﻿#pragma warning disable 169
// ReSharper disable InconsistentNaming

namespace NanoMessageBus.RabbitChannel
{
	using System;
	using System.Collections;
	using System.Globalization;
	using System.Runtime.Serialization;
	using Machine.Specifications;
	using Moq;
	using RabbitMQ.Client;
	using RabbitMQ.Client.Events;
	using RabbitMQ.Client.Exceptions;
	using RabbitMQ.Client.Framing.v0_9_1;
	using It = Machine.Specifications.It;

	[Subject(typeof(RabbitChannel))]
	public class when_opening_a_channel : using_a_channel
	{
		Establish context = () =>
			mockConfiguration.Setup(x => x.DependencyResolver).Returns(new Mock<IDependencyResolver>().Object);

		Because of = () =>
			Initialize();

		It should_create_a_new_transaction = () =>
			channel.CurrentTransaction.ShouldNotBeNull();

		It should_not_yet_have_an_current_message = () =>
			channel.CurrentMessage.ShouldBeNull();

		It should_expose_a_reference_to_the_resolver_from_the_underlying_configuration = () =>
			channel.CurrentResolver.ShouldEqual(mockConfiguration.Object.DependencyResolver);

		It should_expose_a_reference_to_the_underlying_configuration = () =>
			channel.CurrentConfiguration.ShouldEqual(mockConfiguration.Object);
	}

	[Subject(typeof(RabbitChannel))]
	public class when_opening_a_transactional_channel : using_a_channel
	{
		Establish context = () =>
		{
			mockRealChannel.Setup(x => x.TxSelect());
			RequireTransaction(RabbitTransactionType.Full);
		};

		Because of = () =>
			Initialize();

		It should_mark_the_underlying_channel_as_transactional = () =>
			mockRealChannel.Verify(x => x.TxSelect(), Times.Once());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_opening_a_channel_with_a_channel_buffer_size_specified : using_a_channel
	{
		Establish context = () =>
		{
			mockConfiguration.Setup(x => x.ChannelBuffer).Returns(BufferSize);
			mockRealChannel.Setup(x => x.BasicQos(0, BufferSize, false));
		};

		Because of = () =>
			Initialize();

		It should_specify_the_QOS_to_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.BasicQos(0, BufferSize, false), Times.Once());

		const ushort BufferSize = 42;
	}

	[Subject(typeof(RabbitChannel))]
	public class when_opening_the_channel_to_receive : using_a_channel
	{
		Establish context = () =>
		{
			mockConfiguration.Setup(x => x.ReceiveTimeout).Returns(timeout);

			channel = new RabbitChannel(mockRealChannel.Object, mockConfiguration.Object, () =>
			{
				invocations++;
				return mockSubscription.Object;
			});
		};

		Because of = () =>
			channel.Receive(delivery => { });

		It should_call_the_subscription_factory = () =>
			invocations.ShouldEqual(1);

		It should_open_the_subscription_to_receive = () =>
			mockSubscription.Verify(x =>
				x.Receive(timeout, Moq.It.IsAny<Func<BasicDeliverEventArgs, bool>>()));

		static readonly TimeSpan timeout = TimeSpan.FromMilliseconds(250);
		static int invocations;
	}

	[Subject(typeof(RabbitChannel))]
	public class when_opening_the_channel_to_receive_without_provding_a_callback : using_a_channel
	{
		Because of = () =>
			thrown = Catch.Exception(() => channel.Receive(null));

		It should_throw_an_exception = () =>
			thrown.ShouldBeOfType<ArgumentNullException>();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_opening_the_channel_for_receive_more_than_once : using_a_channel
	{
		Establish context = () =>
			channel.Receive(delivery => { });

		Because of = () =>
			thrown = Catch.Exception(() => channel.Receive(delivery => { }));

		It should_throw_an_exception = () =>
			thrown.ShouldBeOfType<InvalidOperationException>();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_opening_a_dispatch_only_channel_for_receive : using_a_channel
	{
		Establish context = () =>
			mockConfiguration.Setup(x => x.DispatchOnly).Returns(true);

		Because of = () =>
			thrown = Catch.Exception(() => channel.Receive(delivery => { }));

		It should_throw_an_exception = () =>
			thrown.ShouldBeOfType<InvalidOperationException>();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_receiving_a_message : using_a_channel
	{
		Establish context = () =>
			mockAdapter.Setup(x => x.Build(message)).Returns(new Mock<ChannelMessage>().Object);

		Because of = () =>
		{
			channel.Receive(deliveryContext => delivery = deliveryContext);
			Receive(message);
		};

		It should_invoke_the_callback_provided = () =>
			delivery.ShouldNotBeNull();

		It should_build_the_ChannelMessage = () =>
			mockAdapter.Verify(x => x.Build(message), Times.Once());

		It should_set_the_ChannelMessage_on_the_channel = () =>
			channel.CurrentMessage.ShouldNotBeNull();

		It should_NOT_mark_the_transaction_a_finished_after_message_processing_is_complete = () =>
			delivery.CurrentTransaction.Finished.ShouldBeFalse();

		It should_purge_the_message_from_the_adapter_cache = () =>
			mockAdapter.Verify(x => x.PurgeFromCache(message), Times.Once());

		static readonly BasicDeliverEventArgs message = new BasicDeliverEventArgs();
		static IDeliveryContext delivery;
	}

	[Subject(typeof(RabbitChannel))]
	public class when_receiving_a_message_after_the_previous_receive_was_committed : using_a_channel
	{
		Establish context = () =>
		{
			mockAdapter.Setup(x => x.Build(message)).Returns(new Mock<ChannelMessage>().Object);
			channel.Receive(deliveryContext => delivery = deliveryContext);
			Receive(message);
			(committed = delivery.CurrentTransaction).Commit();
		};

		Because of = () =>
			Receive(message);

		It should_NOT_reuse_the_committed_transaction = () =>
			delivery.CurrentTransaction.ShouldNotEqual(committed);

		It should_NOT_mark_the_transaction_a_finished_after_message_processing_is_complete = () =>
			delivery.CurrentTransaction.Finished.ShouldBeFalse();

		static readonly BasicDeliverEventArgs message = new BasicDeliverEventArgs();
		static IChannelTransaction committed;
		static IDeliveryContext delivery;
	}

	[Subject(typeof(RabbitChannel))]
	public class when_sending_the_received_message_to_the_loopback_address : using_a_channel
	{
		Establish context = () =>
		{
			mockAdapter.Setup(x => x.Build(message)).Returns(new Mock<ChannelMessage>().Object);

			mockRealChannel
				.Setup(x => x.BasicPublish(Moq.It.IsAny<PublicationAddress>(), message.BasicProperties, message.Body))
				.Callback<PublicationAddress, IBasicProperties, byte[]>((x, y, z) => deliveryAddress = x.ToString());
		};

		Because of = () =>
		{
			channel.Receive(deliveryContext => deliveryContext.Send(BuildEnvelope(deliveryContext)));
			Receive(message);
		};
		static ChannelEnvelope BuildEnvelope(IDeliveryContext delivery)
		{
			return new ChannelEnvelope(delivery.CurrentMessage, new[] { ChannelEnvelope.LoopbackAddress });
		}

		It should_send_the_received_message_for_retry = () =>
			mockRealChannel.Verify(x =>
				x.BasicPublish(Moq.It.IsAny<PublicationAddress>(), message.BasicProperties, message.Body));

		It should_deliver_the_message_to_the_configured_input_queue = () =>
			deliveryAddress.ShouldEqual("direct:///input-queue");

		static readonly BasicDeliverEventArgs message = EmptyMessage();
		static string deliveryAddress;
	}

	[Subject(typeof(RabbitChannel))]
	public class when_sending_a_message_to_dead_letter_address : using_a_channel
	{
		Establish context = () =>
		{
			mockConfiguration
				.Setup(x => x.DeadLetterExchange)
				.Returns(new PublicationAddress("direct", "dead-letters-here", "some-key"));

			mockAdapter
				.Setup(x => x.Build(Moq.It.IsAny<ChannelMessage>(), Moq.It.IsAny<IBasicProperties>()))
				.Returns(EmptyMessage);

			mockRealChannel
				.Setup(x => x.BasicPublish(
					Moq.It.IsAny<PublicationAddress>(),
					Moq.It.IsAny<IBasicProperties>(),
					Moq.It.IsAny<byte[]>()))
				.Callback<PublicationAddress, IBasicProperties, byte[]>((x, y, z) => destination = x);
		};

		Because of = () =>
		{
			var message = new ChannelMessage(Guid.Empty, Guid.Empty, null, null, null);
			var recipients = new[] { ChannelEnvelope.DeadLetterAddress };
			channel.Send(new ChannelEnvelope(message, recipients));
		};

		It should_send_the_message_to_the_configured_dead_letter_exchange = () =>
			destination.ShouldEqual(mockConfiguration.Object.DeadLetterExchange);

		static PublicationAddress destination;
	}

	[Subject(typeof(RabbitChannel))]
	public class when_no_message_is_received_from_the_subscription : using_a_channel
	{
		Establish context = () =>
			channel.Receive(delivery => { });

		Because of = () =>
			Receive(null);

		It should_have_a_fresh_transaction = () =>
			channel.CurrentTransaction.Finished.ShouldBeFalse();

		It should_set_the_CurrentMessage_on_the_channel_to_be_null = () =>
			channel.CurrentMessage.ShouldBeNull();

		It should_not_attempt_to_process_the_null_message = () =>
			mockAdapter.Verify(x => x.Build(null), Times.Never());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_the_message_received_has_expired : using_a_channel
	{
		Establish context = () =>
		{
			message = new BasicDeliverEventArgs
			{
				BasicProperties = new BasicProperties
				{
					Expiration = SystemTime.EpochTime.ToString(CultureInfo.InvariantCulture)
				},
				Body = new byte[] { 1, 2, 3, 4 }
			};

			mockConfiguration.Setup(x => x.DeadLetterExchange).Returns(address);
			mockAdapter.Setup(x => x.Build(message)).Throws(new DeadLetterException());

			RequireTransaction(RabbitTransactionType.Full);
			Initialize();
		};

		Because of = () =>
		{
			channel.Receive(deliveryContext => { });
			Receive(message);
		};

		It should_dispatch_the_message_to_the_configured_dead_letter_exchange = () =>
			mockRealChannel.Verify(x =>
				x.BasicPublish(address, message.BasicProperties, message.Body), Times.Once());

		It should_acknowledge_message_receipt_to_the_underlying_channel = () =>
			mockSubscription.Verify(x => x.AcknowledgeMessage(), Times.Once());

		It should_commit_the_outstanding_transaction_against_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.TxCommit(), Times.Once());

		It should_purge_the_message_from_the_adapter_cache = () =>
			mockAdapter.Verify(x => x.PurgeFromCache(message), Times.Once());

		static BasicDeliverEventArgs message;
		static readonly PublicationAddress address =
			new PublicationAddress(string.Empty, string.Empty, string.Empty);
	}

	[Subject(typeof(RabbitChannel))]
	public class when_the_receive_callback_considers_the_message_to_be_a_dead_letter : using_a_channel
	{
		Establish context = () =>
		{
			mockConfiguration.Setup(x => x.DeadLetterExchange).Returns(address);

			RequireTransaction(RabbitTransactionType.Full);
			Initialize();
		};

		Because of = () =>
		{
			channel.Receive(deliveryContext => { throw new DeadLetterException(); });
			Receive(message);
		};

		It should_dispatch_the_message_to_the_configured_dead_letter_exchange = () =>
			mockRealChannel.Verify(x =>
				x.BasicPublish(address, message.BasicProperties, message.Body), Times.Once());

		It should_acknowledge_message_receipt_to_the_underlying_channel = () =>
			mockSubscription.Verify(x => x.AcknowledgeMessage(), Times.Once());

		It should_commit_the_outstanding_transaction_against_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.TxCommit(), Times.Once());

		It should_purge_the_message_from_the_adapter_cache = () =>
			mockAdapter.Verify(x => x.PurgeFromCache(message), Times.Once());

		static readonly BasicDeliverEventArgs message = EmptyMessage();
		static readonly PublicationAddress address =
			new PublicationAddress(string.Empty, string.Empty, string.Empty);
	}

	[Subject(typeof(RabbitChannel))]
	public class when_an_expired_message_is_received_but_no_dead_letter_exchange_is_configured : using_a_channel
	{
		Establish context = () =>
		{
			message = new BasicDeliverEventArgs
			{
				BasicProperties = new BasicProperties { Expiration = SystemTime.EpochTime.ToString(CultureInfo.InvariantCulture) }
			};

			mockConfiguration.Setup(x => x.DeadLetterExchange).Returns((PublicationAddress)null);
			mockAdapter.Setup(x => x.Build(message)).Throws(new DeadLetterException());

			Initialize();
		};

		Because of = () =>
		{
			channel.Receive(deliveryContext => { });
			Receive(message);
		};

		It should_drop_the_message = () =>
			mockRealChannel.Verify(x =>
				x.BasicPublish(Moq.It.IsAny<PublicationAddress>(), message.BasicProperties, message.Body), Times.Never());

		static BasicDeliverEventArgs message;
	}

	[Subject(typeof(RabbitChannel))]
	public class when_the_handling_of_a_message_throws_an_exception : using_a_channel
	{
		Establish context = () =>
		{
			mockConfiguration.Setup(x => x.MaxAttempts).Returns(1); // allow one failure

			mockRealChannel.Setup(x => x.TxRollback());
			mockSubscription.Setup(x => x.RetryMessage(message));

			RequireTransaction(RabbitTransactionType.Full);
			Initialize();
		};

		Because of = () =>
		{
			channel.Receive(delivery => { throw new Exception("Message handling failed"); });
			Receive(message);
		};

		It should_add_the_message_to_the_retry_queue = () =>
			mockSubscription.Verify(x => x.RetryMessage(message));

		It should_increment_the_failure_count_on_the_message = () =>
			message.GetAttemptCount().ShouldEqual(1);

		It should_rollback_the_outstanding_transaction_against_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.TxRollback(), Times.Once());

		static readonly BasicDeliverEventArgs message = new BasicDeliverEventArgs();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_message_processing_has_exceeded_the_maximum_number_of_configured_attempts : using_a_channel
	{
		Establish context = () =>
		{
			mockConfiguration.Setup(x => x.PoisonMessageExchange).Returns(address);
			mockConfiguration.Setup(x => x.MaxAttempts).Returns(FirstFailureIsPoisonMessage);

			RequireTransaction(RabbitTransactionType.Full);
			Initialize();
		};

		Because of = () =>
		{
			channel.Receive(delivery => { throw raise; });
			Receive(message);
		};

		It should_append_the_exception_to_the_message = () =>
			mockAdapter.Verify(x => x.AppendException(message, raise));

		It should_clear_the_failure_count_on_the_message = () =>
			message.GetAttemptCount().ShouldEqual(0);

		It should_dispatch_the_message_to_the_configured_poison_message_exchange = () =>
			mockRealChannel.Verify(x =>
				x.BasicPublish(address, message.BasicProperties, message.Body), Times.Once());

		It should_acknowledge_message_receipt_to_the_underlying_channel = () =>
			mockSubscription.Verify(x => x.AcknowledgeMessage(), Times.Once());

		It should_commit_the_outstanding_transaction_against_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.TxCommit(), Times.Once());

		It should_purge_the_message_from_the_adapter_cache = () =>
			mockAdapter.Verify(x => x.PurgeFromCache(message));

		const int FirstFailureIsPoisonMessage = 0;
		static readonly Exception raise = new Exception();
		static readonly BasicDeliverEventArgs message = new BasicDeliverEventArgs();
		static readonly PublicationAddress address =
			new PublicationAddress(string.Empty, string.Empty, string.Empty);
	}

	[Subject(typeof(RabbitChannel))]
	public class when_the_handling_of_a_message_throws_a_SerializationException : using_a_channel
	{
		Establish context = () =>
		{
			message = new BasicDeliverEventArgs
			{
				BasicProperties = new BasicProperties(),
				Body = new byte[] { 0, 1, 2, 3, 4 }
			};

			mockSubscription.Setup(x => x.AcknowledgeMessage());

			mockRealChannel.Setup(x => x.TxCommit());
			mockRealChannel.Setup(x => x.BasicPublish(address, message.BasicProperties, message.Body));
			mockConfiguration.Setup(x => x.PoisonMessageExchange).Returns(address);
			mockAdapter.Setup(x => x.Build(message)).Throws(serializationException);

			RequireTransaction(RabbitTransactionType.Full);
			Initialize();
		};

		Because of = () =>
		{
			channel.Receive(delivery => { });
			Receive(message);
		};

		It should_append_the_exception_to_the_message = () =>
			mockAdapter.Verify(x => x.AppendException(message, serializationException));

		It should_dispatch_the_message_to_the_configured_poison_message_exchange = () =>
			mockRealChannel.Verify(x =>
				x.BasicPublish(address, message.BasicProperties, message.Body), Times.Once());

		It should_acknowledge_the_poison_message_from_the_receiving_queue_when_the_channels_uses_ack = () =>
			mockSubscription.Verify(x => x.AcknowledgeMessage(), Times.Once());

		It should_commit_the_transaction_on_fully_transactional_channels = () =>
			mockRealChannel.Verify(x => x.TxCommit(), Times.Once());

		It should_purge_the_message_from_the_adapter_cache = () =>
			mockAdapter.Verify(x => x.PurgeFromCache(message));

		static BasicDeliverEventArgs message;
		static readonly Exception serializationException = new SerializationException();
		static readonly PublicationAddress address =
			new PublicationAddress(string.Empty, string.Empty, string.Empty);
	}

	[Subject(typeof(RabbitChannel))]
	public class when_the_handling_of_a_message_throws_a_ChannelConnectionException : using_a_channel
	{
		Establish context = () =>
			channel.Receive(delivery => { throw connectionException; });

		Because of = () =>
			thrown = Catch.Exception(() => Receive(message));

		It should_purge_the_message_from_the_adapter_cache = () =>
			mockAdapter.Verify(x => x.PurgeFromCache(message));

		It should_throw_the_exception = () =>
			thrown.ShouldBeOfType<ChannelConnectionException>();

		It should_mark_the_transaction_a_finished = () =>
			channel.CurrentTransaction.Finished.ShouldBeTrue();

		static readonly BasicDeliverEventArgs message = new BasicDeliverEventArgs();
		static readonly Exception connectionException = new ChannelConnectionException();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_sending_a_null_message : using_a_channel
	{
		Because of = () =>
			thrown = Catch.Exception(() => channel.Send(null));

		It should_throw_an_exception = () =>
			thrown.ShouldBeOfType<ArgumentNullException>();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_sending_a_message : using_a_channel
	{
		Establish context = () =>
		{
			mockEnvelope = new Mock<ChannelEnvelope>();
			mockEnvelope.Setup(x => x.Message).Returns(new Mock<ChannelMessage>().Object);
			mockEnvelope.Setup(x => x.Recipients).Returns(new[]
			{
				ChannelEnvelope.LoopbackAddress,
				ChannelEnvelope.LoopbackAddress
			});

			rabbitMessage = new BasicDeliverEventArgs
			{
				BasicProperties = mockRealChannel.Object.CreateBasicProperties(),
				Body = new byte[] { 0, 1, 2, 3, 4 }
			};

			mockAdapter
				.Setup(x => x.Build(mockEnvelope.Object.Message, mockRealChannel.Object.CreateBasicProperties()))
				.Returns(rabbitMessage);

			channel.CurrentTransaction.Dispose(); // mark the previous transaction as complete
		};

		Because of = () =>
		{
			channel.Send(mockEnvelope.Object);
			channel.CurrentTransaction.Commit();
		};

		It should_build_a_wire_message_from_the_ChannelMessage_provided = () =>
			mockAdapter.Verify(x => x.Build(mockEnvelope.Object.Message, mockRealChannel.Object.CreateBasicProperties()), Times.Once());

		It should_dispatch_the_message_to_the_recipients_specified = () =>
			mockRealChannel.Verify(x => x.BasicPublish(
				Moq.It.IsAny<PublicationAddress>(),
				rabbitMessage.BasicProperties,
				rabbitMessage.Body), Times.Exactly(mockEnvelope.Object.Recipients.Count));

		It should_mark_the_current_transaction_as_finished = () =>
			channel.CurrentTransaction.Finished.ShouldBeTrue();

		static Mock<ChannelEnvelope> mockEnvelope;
		static BasicDeliverEventArgs rabbitMessage;
	}

	[Subject(typeof(RabbitChannel))]
	public class when_sending_a_message_to_the_default_exchange : using_a_channel
	{
		Establish context = () =>
		{
			var properties = new BasicProperties();

			mockRealChannel
				.Setup(x => x.CreateBasicProperties())
				.Returns(properties);

			mockAdapter
				.Setup(x => x.Build(envelope.Message, properties))
				.Returns(new BasicDeliverEventArgs());

			mockRealChannel
				.Setup(x => x.BasicPublish(
					Moq.It.IsAny<PublicationAddress>(),
					Moq.It.IsAny<IBasicProperties>(),
					Moq.It.IsAny<byte[]>()))
				.Callback<PublicationAddress, IBasicProperties, byte[]>((address, p, b) => result = address);
		};

		Because of = () =>
			channel.Send(envelope);

		It should_have_a_publication_address_with_an_empty_exchange = () =>
			result.ExchangeName.ShouldBeEmpty();

		It should_have_a_publication_address_with_a_direct_exchange_type = () =>
			result.ExchangeType.ShouldEqual(ExchangeType.Direct);

		It should_have_a_publication_address_with_the_correct_routing_key = () =>
			result.RoutingKey.ShouldEqual("MyRoutingKey");

		static readonly Uri recipient = new Uri("fanout://default/MyRoutingKey");
		static readonly ChannelEnvelope envelope = SimpleEnvelope(recipient);
		static PublicationAddress result;
	}

	[Subject(typeof(RabbitChannel))]
	public class when_acknowledging_a_message_against_an_acknowledge_only_channel : using_a_channel
	{
		Establish context = () =>
		{
			RequireTransaction(RabbitTransactionType.Acknowledge);
			mockSubscription.Setup(x => x.AcknowledgeMessage());
			Initialize();

			channel.Receive(delivery => { });
		};

		Because of = () =>
			channel.AcknowledgeMessage();

		It should_ack_against_the_underlying_subscription = () =>
			mockSubscription.Verify(x => x.AcknowledgeMessage(), Times.Once());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_acknowledging_a_message_with_no_transaction : using_a_channel
	{
		Establish context = () =>
			channel.Receive(delivery => { });

		Because of = () =>
			channel.AcknowledgeMessage();

		It should_NOT_ack_against_the_underlying_subscription = () =>
			mockSubscription.Verify(x => x.AcknowledgeMessage(), Times.Never());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_acknowledging_a_message_on_a_full_transaction : using_a_channel
	{
		Establish context = () =>
		{
			RequireTransaction(RabbitTransactionType.Full);
			mockSubscription.Setup(x => x.AcknowledgeMessage());
			Initialize();

			channel.Receive(delivery => { });
		};

		Because of = () =>
			channel.AcknowledgeMessage();

		It should_ack_against_the_underlying_subscription = () =>
			mockSubscription.Verify(x => x.AcknowledgeMessage(), Times.Once());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_acknowledging_without_first_opening_for_receive : using_a_channel
	{
		Establish context = () =>
		{
			RequireTransaction(RabbitTransactionType.Acknowledge);
			Initialize();
		};

		Because of = () =>
			channel.AcknowledgeMessage();

		It should_NOT_ack_against_the_underlying_subscription = () =>
			mockSubscription.Verify(x => x.AcknowledgeMessage(), Times.Never());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_committing_a_transaction_against_a_non_transactional_channel : using_a_channel
	{
		Establish context = () =>
			mockRealChannel.Setup(x => x.TxCommit());

		Because of = () =>
			channel.CommitTransaction();

		It should_NOT_invoke_commit_against_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.TxCommit(), Times.Never());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_committing_a_transaction_against_an_acknowledge_channel : using_a_channel
	{
		Establish context = () =>
		{
			RequireTransaction(RabbitTransactionType.Acknowledge);
			Initialize();

			mockRealChannel.Setup(x => x.TxCommit());
		};

		Because of = () =>
			channel.CommitTransaction();

		It should_NOT_invoke_commit_against_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.TxCommit(), Times.Never());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_committing_a_transaction_against_a_transactional_channel : using_a_channel
	{
		Establish context = () =>
		{
			RequireTransaction(RabbitTransactionType.Full);
			Initialize();

			mockRealChannel.Setup(x => x.TxCommit());
		};

		Because of = () =>
			channel.CommitTransaction();

		It should_NOT_invoke_commit_against_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.TxCommit(), Times.Once());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_rolling_back_a_transaction_against_a_non_transactional_channel : using_a_channel
	{
		Establish context = () =>
			mockRealChannel.Setup(x => x.TxRollback());

		Because of = () =>
			channel.RollbackTransaction();

		It should_NOT_invoke_commit_against_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.TxRollback(), Times.Never());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_rolling_back_a_transaction_against_an_acknowledge_channel : using_a_channel
	{
		Establish context = () =>
		{
			RequireTransaction(RabbitTransactionType.Acknowledge);
			Initialize();

			mockRealChannel.Setup(x => x.TxRollback());
		};

		Because of = () =>
			channel.RollbackTransaction();

		It should_NOT_invoke_commit_against_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.TxRollback(), Times.Never());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_rolling_back_a_transaction_against_a_transactional_channel : using_a_channel
	{
		Establish context = () =>
		{
			RequireTransaction(RabbitTransactionType.Full);
			Initialize();

			mockRealChannel.Setup(x => x.TxRollback());
		};

		Because of = () =>
			channel.RollbackTransaction();

		It should_NOT_invoke_commit_against_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.TxRollback(), Times.Once());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_dispatching_a_message_throws_an_OperationInterruptedException : using_a_channel
	{
		Establish context = () =>
		{
			mockEnvelope = new Mock<ChannelEnvelope>();
			mockEnvelope.Setup(x => x.Recipients).Returns(new[] { new Uri("ampq://test/test") });
			mockEnvelope.Setup(x => x.Message).Returns(new Mock<ChannelMessage>().Object);

			mockAdapter
				.Setup(x => x.Build(mockEnvelope.Object.Message, Moq.It.IsAny<IBasicProperties>()))
				.Returns(EmptyMessage);

			mockRealChannel
				.Setup(x => x.BasicPublish(
					Moq.It.IsAny<PublicationAddress>(),
					Moq.It.IsAny<IBasicProperties>(),
					Moq.It.IsAny<byte[]>()))
				.Throws(new OperationInterruptedException(null));
		};

		Because of = () =>
			thrown = Catch.Exception(() => channel.Send(mockEnvelope.Object));

		It should_throw_a_ChannelConnectionException = () =>
			thrown.ShouldBeOfType<ChannelConnectionException>();

		static Mock<ChannelEnvelope> mockEnvelope;
	}

	[Subject(typeof(RabbitChannel))]
	public class when_committing_a_transaction_throws_an_OperationInterruptedException : using_a_channel
	{
		Establish context = () =>
		{
			mockRealChannel.Setup(x => x.TxCommit()).Throws(new OperationInterruptedException(null));

			RequireTransaction(RabbitTransactionType.Full);
			Initialize();
		};

		Because of = () =>
			thrown = Catch.Exception(() => channel.CommitTransaction());

		It should_throw_a_ChannelConnectionException = () =>
			thrown.ShouldBeOfType<ChannelConnectionException>();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_rolling_back_a_transaction_throws_an_OperationInterruptedException : using_a_channel
	{
		Establish context = () =>
		{
			mockRealChannel.Setup(x => x.TxRollback()).Throws(new OperationInterruptedException(null));

			RequireTransaction(RabbitTransactionType.Full);
			Initialize();
		};

		Because of = () =>
			thrown = Catch.Exception(() => channel.RollbackTransaction());

		It should_throw_a_ChannelConnectionException = () =>
			thrown.ShouldBeOfType<ChannelConnectionException>();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_disposing_a_channel : using_a_channel
	{
		Establish context = () =>
			mockRealChannel.Setup(x => x.Dispose());

		Because of = () =>
			channel.Dispose();

		It should_dispose_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.Abort(), Times.Once());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_disposing_a_transactional_channel : using_a_channel
	{
		Establish context = () =>
		{
			mockRealChannel.Setup(x => x.Dispose());

			RequireTransaction(RabbitTransactionType.Full);
			Initialize();
		};

		Because of = () =>
			channel.Dispose();

		It should_dispose_the_active_transaction_before_the_channel_is_disposed = () =>
			mockRealChannel.Verify(x => x.TxRollback(), Times.Once());

		It should_dispose_the_underlying_channel = () =>
			mockRealChannel.Verify(x => x.Abort(), Times.Once());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_disposing_a_channel_with_a_subscription : using_a_channel
	{
		Establish context = () =>
		{
			mockSubscription.Setup(x => x.Dispose());
			channel.Receive(delivery => { });
		};

		Because of = () =>
			channel.Dispose();

		It should_dispose_the_subscription = () =>
			mockSubscription.Verify(x => x.Dispose(), Times.Once());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_dispose_is_called_multiple_times : using_a_channel
	{
		Establish context = () =>
			mockRealChannel.Setup(x => x.Dispose());

		Because of = () =>
		{
			channel.Dispose();
			channel.Dispose();
		};

		It should_only_dispose_the_underlying_resources_once = () =>
			mockRealChannel.Verify(x => x.Abort(), Times.Once());
	}

	[Subject(typeof(RabbitChannel))]
	public class when_attempting_to_send_through_a_disposed_channel : using_a_channel
	{
		Establish context = () =>
			channel.Dispose();

		Because of = () =>
			thrown = Catch.Exception(() => channel.Send(new Mock<ChannelEnvelope>().Object));

		It should_throw_an_exception = () =>
			thrown.ShouldBeOfType<ObjectDisposedException>();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_attempting_to_receive_through_a_disposed_channel : using_a_channel
	{
		Establish context = () =>
			channel.Dispose();

		Because of = () =>
			thrown = Catch.Exception(() => channel.Receive(context => { }));

		It should_throw_an_exception = () =>
			thrown.ShouldBeOfType<ObjectDisposedException>();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_attempting_to_acknowledge_a_message_against_a_disposed_channel : using_a_channel
	{
		Establish context = () =>
			channel.Dispose();

		Because of = () =>
			thrown = Catch.Exception(() => channel.RollbackTransaction());

		It should_throw_an_exception = () =>
			thrown.ShouldBeOfType<ObjectDisposedException>();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_attempting_to_commit_a_transaction_against_a_disposed_channel : using_a_channel
	{
		Establish context = () =>
			channel.Dispose();

		Because of = () =>
			thrown = Catch.Exception(() => channel.CommitTransaction());

		It should_throw_an_exception = () =>
			thrown.ShouldBeOfType<ObjectDisposedException>();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_attempting_to_rollback_a_transaction_against_a_disposed_channel : using_a_channel
	{
		Establish context = () =>
			channel.Dispose();

		Because of = () =>
			thrown = Catch.Exception(() => channel.RollbackTransaction());

		It should_throw_an_exception = () =>
			thrown.ShouldBeOfType<ObjectDisposedException>();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_initiating_receive_on_a_shutdown_channel : using_a_channel
	{
		Establish context = () =>
			channel.BeginShutdown();

		Because of = () =>
			thrown = Catch.Exception(() => channel.Receive(context => { }));

		It should_throw_an_exception = () =>
			thrown.ShouldBeOfType<ChannelShutdownException>();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_receiving_a_message_on_a_shutdown_channel : using_a_channel
	{
		Establish context = () =>
		{
			channel.Receive(delivery => { });
			channel.BeginShutdown();
		};

		Because of = () =>
			Receive(message);

		It should_not_process_the_message = () =>
			mockAdapter.Verify(x => x.Build(message), Times.Never());

		static readonly BasicDeliverEventArgs message = new BasicDeliverEventArgs();
	}

	[Subject(typeof(RabbitChannel))]
	public class when_attempting_to_send_on_full_duplex_channel_that_is_shutting_down : using_a_channel
	{
		Establish context = () =>
		{
			channel.Receive(delivery => { }); // makes the channel full duplex

			var mockEnvelope = new Mock<ChannelEnvelope>();
			mockEnvelope.Setup(x => x.Recipients).Returns(new Uri[0]);
			mockAdapter.Setup(x => x.Build(mockEnvelope.Object.Message, mockRealChannel.Object.CreateBasicProperties()));
			envelope = mockEnvelope.Object;

			channel.BeginShutdown();
		};

		Because of = () =>
			channel.Send(envelope);

		It should_allow_the_dispatch_to_proceed_so_that_the_transaction_can_complete = () =>
			mockAdapter.Verify(x => x.Build(envelope.Message, mockRealChannel.Object.CreateBasicProperties()), Times.Once());

		static ChannelEnvelope envelope;
	}

	[Subject(typeof(RabbitChannel))]
	public class when_attempting_to_send_on_a_send_only_channel_that_is_shutting_down : using_a_channel
	{
		Establish context = () =>
			channel.BeginShutdown();

		Because of = () =>
			thrown = Catch.Exception(() => channel.Send(new Mock<ChannelEnvelope>().Object));

		It should_throw_an_exception = () =>
			thrown.ShouldBeOfType<ChannelShutdownException>();
	}

	public abstract class using_a_channel
	{
		Establish context = () =>
		{
			mockRealChannel = new Mock<IModel>();
			mockAdapter = new Mock<RabbitMessageAdapter>();
			mockConfiguration = new Mock<RabbitChannelGroupConfiguration>();
			mockSubscription = new Mock<RabbitSubscription>();

			var timeout = TimeSpan.FromMilliseconds(100);
			mockConfiguration.Setup(x => x.ReceiveTimeout).Returns(timeout);
			mockConfiguration.Setup(x => x.MessageAdapter).Returns(mockAdapter.Object);
			mockConfiguration.Setup(x => x.InputQueue).Returns(InputQueueName);
			mockSubscription
				.Setup(x => x.Receive(timeout, Moq.It.IsAny<Func<BasicDeliverEventArgs, bool>>()))
				.Callback<TimeSpan, Func<BasicDeliverEventArgs, bool>>((first, second) => { dispatch = second; });

			mockRealChannel.Setup(x => x.CreateBasicProperties()).Returns(new BasicProperties());

			mockAdapter
				.Setup(x => x.Build(Moq.It.IsAny<BasicDeliverEventArgs>()))
				.Returns(new Mock<ChannelMessage>().Object);

			RequireTransaction(RabbitTransactionType.None);
			Initialize();
		};

		protected static void RequireTransaction(RabbitTransactionType transactionType)
		{
			mockConfiguration.Setup(x => x.TransactionType).Returns(transactionType);
		}
		protected static void Initialize()
		{
			channel = new RabbitChannel(
				mockRealChannel.Object, mockConfiguration.Object, () => mockSubscription.Object);
		}
		protected static void Receive(BasicDeliverEventArgs message)
		{
			dispatch(message);
		}
		protected static BasicDeliverEventArgs EmptyMessage()
		{
			return new BasicDeliverEventArgs
			{
				Body = new byte[0],
				BasicProperties = new BasicProperties { Headers = new Hashtable() }
			};
		}
		protected static ChannelEnvelope SimpleEnvelope(Uri recipient)
		{
			return new ChannelEnvelope(
				new ChannelMessage(Guid.NewGuid(), Guid.NewGuid(), null, null, new object[] { 1, 2, 3 }),
				new[] { recipient });
		}

		protected const string DefaultChannelGroup = "some group name";
		protected const string InputQueueName = "input-queue";
		protected static Mock<IModel> mockRealChannel;
		protected static Mock<RabbitMessageAdapter> mockAdapter;
		protected static Mock<RabbitChannelGroupConfiguration> mockConfiguration;
		protected static Mock<RabbitSubscription> mockSubscription;
		protected static RabbitChannel channel;
		protected static Exception thrown;
		static Func<BasicDeliverEventArgs, bool> dispatch;
	}
}

// ReSharper enable InconsistentNaming
#pragma warning restore 169
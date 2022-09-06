using System;
using System.Collections.Generic;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Paramore.Brighter.Logging;
using Paramore.Brighter.MessagingGateway.AzureServiceBus.AzureServiceBusWrappers;

namespace Paramore.Brighter.MessagingGateway.AzureServiceBus
{
    /// <summary>
    /// Implementation of <see cref="IAmAMessageConsumer"/> using Azure Service Bus for Transport.
    /// </summary>
    public class AzureServiceBusConsumer : IAmAMessageConsumer
    {
        private readonly string _queueOrTopicName;
        private readonly IAmAMessageProducerSync _messageProducerSync;
        private readonly IAdministrationClientWrapper _administrationClientWrapper;
        private readonly IServiceBusReceiverProvider _serviceBusReceiverProvider;
        private readonly int _batchSize;
        private IServiceBusReceiverWrapper _serviceBusReceiver;
        private readonly string _subscriptionName;
        private bool _subscriptionCreated;
        private static readonly ILogger s_logger = ApplicationLogging.CreateLogger<AzureServiceBusConsumer>();
        private readonly OnMissingChannel _makeChannel;
        private readonly AzureServiceBusSubscriptionConfiguration _subscriptionConfiguration;
        private readonly ServiceBusReceiveMode _receiveMode;
        private readonly string _entityPath;

        /// <summary>
        /// Initializes an Instance of <see cref="AzureServiceBusConsumer"/>
        /// </summary>
        /// <param name="queueOrTopicName">The name of the Topic.</param>
        /// <param name="subscriptionName">The name of the Subscription on the Topic.</param>
        /// <param name="messageProducerSync">An instance of the Messaging Producer used for Requeue.</param>
        /// <param name="administrationClientWrapper">An Instance of Administration Client Wrapper.</param>
        /// <param name="serviceBusReceiverProvider">An Instance of <see cref="ServiceBusReceiverProvider"/>.</param>
        /// <param name="batchSize">How many messages to receive at a time.</param>
        /// <param name="receiveMode">The mode in which to Receive.</param>
        /// <param name="makeChannels">The mode in which to make Channels.</param>
        /// <param name="subscriptionConfiguration">The configuration options for the subscriptions.</param>
        public AzureServiceBusConsumer(string queueOrTopicName, string subscriptionName,
            IAmAMessageProducerSync messageProducerSync, IAdministrationClientWrapper administrationClientWrapper,
            IServiceBusReceiverProvider serviceBusReceiverProvider, int batchSize = 10,
            ServiceBusReceiveMode receiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
            OnMissingChannel makeChannels = OnMissingChannel.Create,
            AzureServiceBusSubscriptionConfiguration subscriptionConfiguration = default)
        {
            _subscriptionName = subscriptionName;
            _queueOrTopicName = queueOrTopicName;
            _messageProducerSync = messageProducerSync;
            _administrationClientWrapper = administrationClientWrapper;
            _serviceBusReceiverProvider = serviceBusReceiverProvider;
            _batchSize = batchSize;
            _makeChannel = makeChannels;
            _subscriptionConfiguration = subscriptionConfiguration ?? new AzureServiceBusSubscriptionConfiguration();
            _receiveMode = receiveMode;
            _entityPath = queueOrTopicName +
                          (_subscriptionConfiguration.UseQueue ? $"/{subscriptionName}" : string.Empty);
            GetMessageReceiverProvider();
        }

        /// <summary>
        /// Receives the specified queue name.
        /// An abstraction over a third-party messaging library. Used to read messages from the broker and to acknowledge the processing of those messages or requeue them.
        /// Used by a <see cref="Channel"/> to provide access to a third-party message queue.
        /// </summary>
        /// <param name="timeoutInMilliseconds">The timeout in milliseconds.</param>
        /// <returns>Message.</returns>
        public Message[] Receive(int timeoutInMilliseconds)
        {
            s_logger.LogDebug(
                "Preparing to retrieve next message(s) from entity {EntityPath} with timeout {Timeout} and batch size {BatchSize}.",
                _entityPath, timeoutInMilliseconds, _batchSize);

            IEnumerable<IBrokeredMessageWrapper> messages;
            EnsureServiceBusEntityExists();

            var messagesToReturn = new List<Message>();

            try
            {
                messages = _serviceBusReceiver.Receive(_batchSize, TimeSpan.FromMilliseconds(timeoutInMilliseconds))
                    .GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                if (_serviceBusReceiver.IsClosedOrClosing)
                {
                    s_logger.LogDebug("Message Receiver is closing...");
                    var message = new Message(new MessageHeader(Guid.NewGuid(), _queueOrTopicName, MessageType.MT_QUIT),
                        new MessageBody(string.Empty));
                    messagesToReturn.Add(message);
                    return messagesToReturn.ToArray();
                }

                s_logger.LogError(e, "Failing to receive messages.");

                //The connection to Azure Service bus may have failed so we re-establish the connection.
                GetMessageReceiverProvider();

                throw new ChannelFailureException("Failing to receive messages.", e);
            }

            foreach (IBrokeredMessageWrapper azureServiceBusMessage in messages)
            {
                Message message = MapToBrighterMessage(azureServiceBusMessage);
                messagesToReturn.Add(message);
            }

            return messagesToReturn.ToArray();
        }

        /// <summary>
        /// Requeues the specified message.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="delayMilliseconds">Number of milliseconds to delay delivery of the message.</param>
        /// <returns>True if the message should be acked, false otherwise</returns>
        public bool Requeue(Message message, int delayMilliseconds)
        {
            var topic = message.Header.Topic;

            s_logger.LogInformation("Requeuing message with topic {Topic} and id {Id}.", topic, message.Id);

            if (delayMilliseconds > 0)
            {
                _messageProducerSync.SendWithDelay(message, delayMilliseconds);
            }
            else
            {
                _messageProducerSync.Send(message);
            }

            Acknowledge(message);

            return true;
        }

        /// <summary>
        /// Acknowledges the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Acknowledge(Message message)
        {
            //Only ACK if ReceiveMode is Peek
            if (_receiveMode.Equals(ServiceBusReceiveMode.PeekLock))
            {
                try
                {
                    EnsureServiceBusEntityExists();
                    var lockToken = message.Header.Bag[ASBConstants.LockTokenHeaderBagKey].ToString();

                    if (string.IsNullOrEmpty(lockToken))
                        throw new Exception($"LockToken for message with id {message.Id} is null or empty");
                    s_logger.LogDebug("Acknowledging Message with Id {Id} Lock Token : {LockToken}", message.Id,
                        lockToken);

                    _serviceBusReceiver.Complete(lockToken).Wait();
                }
                catch (AggregateException ex)
                {
                    if (ex.InnerException is ServiceBusException asbException)
                        HandleASBException(asbException, message.Id);
                    else
                    {
                        s_logger.LogError(ex, "Error completing peak lock on message with id {Id}", message.Id);
                        throw;
                    }
                }
                catch (ServiceBusException ex)
                {
                    HandleASBException(ex, message.Id);
                }
                catch (Exception ex)
                {
                    s_logger.LogError(ex, "Error completing peak lock on message with id {Id}", message.Id);
                    throw;
                }
            }
            else
            {
                s_logger.LogDebug(
                    "Completing with Id {Id} is not possible due to receive Mode being set to {ReceiveMode}",
                    message.Id, _receiveMode);
            }
        }

        /// <summary>
        /// Rejects the specified message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Reject(Message message)
        {
            //Only Reject if ReceiveMode is Peek
            if (_receiveMode.Equals(ServiceBusReceiveMode.PeekLock))
            {
                try
                {
                    EnsureServiceBusEntityExists();
                    var lockToken = message.Header.Bag[ASBConstants.LockTokenHeaderBagKey].ToString();

                    if (string.IsNullOrEmpty(lockToken))
                        throw new Exception($"LockToken for message with id {message.Id} is null or empty");
                    s_logger.LogDebug("Dead Lettering Message with Id {Id} Lock Token : {LockToken}", message.Id,
                        lockToken);

                    _serviceBusReceiver.DeadLetter(lockToken).Wait();
                }
                catch (Exception ex)
                {
                    s_logger.LogError(ex, "Error Dead Lettering message with id {Id}", message.Id);
                    throw;
                }
            }
            else
            {
                s_logger.LogWarning(
                    "Dead Lettering Message with Id {Id} is not possible due to receive Mode being set to {ReceiveMode}",
                    message.Id, _receiveMode);
            }
        }

        /// <summary>
        /// Purges the specified queue name.
        /// </summary>
        public void Purge()
        {
            s_logger.LogWarning("Purge method NOT IMPLEMENTED.");
        }

        /// <summary>
        /// Dispose of the Consumer.
        /// </summary>
        public void Dispose()
        {
            s_logger.LogInformation("Disposing the consumer...");
            _serviceBusReceiver.Close();
            s_logger.LogInformation("Consumer disposed.");
        }

        private void GetMessageReceiverProvider()
        {
            s_logger.LogInformation(
                "Getting message receiver provider for topic {Topic} and subscription {ChannelName} with receive Mode {ReceiveMode}...",
                _queueOrTopicName, _subscriptionName, _receiveMode);
            try
            {
                _serviceBusReceiver =
                    _serviceBusReceiverProvider.Get(_queueOrTopicName, _subscriptionName, _receiveMode);
            }
            catch (Exception e)
            {
                s_logger.LogError(e,
                    "Failed to get message receiver provider for topic {Topic} and subscription {ChannelName}.",
                    _queueOrTopicName, _subscriptionName);
            }
        }

        private Message MapToBrighterMessage(IBrokeredMessageWrapper azureServiceBusMessage)
        {
            if (azureServiceBusMessage.MessageBodyValue == null)
            {
                s_logger.LogWarning(
                    "Null message body received from topic {Topic} via subscription {ChannelName}.",
                    _queueOrTopicName, _subscriptionName);
            }

            var messageBody =
                System.Text.Encoding.Default.GetString(azureServiceBusMessage.MessageBodyValue ?? Array.Empty<byte>());
            s_logger.LogDebug("Received message from topic {Topic} via subscription {ChannelName} with body {Request}.",
                _queueOrTopicName, _subscriptionName, messageBody);
            MessageType messageType = GetMessageType(azureServiceBusMessage);
            var handledCount = GetHandledCount(azureServiceBusMessage);
            var headers = new MessageHeader(azureServiceBusMessage.Id, _queueOrTopicName, messageType, DateTime.UtcNow,
                handledCount, 0, azureServiceBusMessage.CorrelationId, contentType: azureServiceBusMessage.ContentType);
            if (_receiveMode.Equals(ServiceBusReceiveMode.PeekLock))
                headers.Bag.Add(ASBConstants.LockTokenHeaderBagKey, azureServiceBusMessage.LockToken);
            foreach (var property in azureServiceBusMessage.ApplicationProperties)
            {
                headers.Bag.Add(property.Key, property.Value);
            }

            var message = new Message(headers, new MessageBody(messageBody));
            return message;
        }

        private static MessageType GetMessageType(IBrokeredMessageWrapper azureServiceBusMessage)
        {
            if (azureServiceBusMessage.ApplicationProperties == null ||
                !azureServiceBusMessage.ApplicationProperties.ContainsKey(ASBConstants.MessageTypeHeaderBagKey))
                return MessageType.MT_EVENT;

            if (Enum.TryParse(
                    azureServiceBusMessage.ApplicationProperties[ASBConstants.MessageTypeHeaderBagKey].ToString(), true,
                    out MessageType messageType))
                return messageType;

            return MessageType.MT_EVENT;
        }

        private static int GetHandledCount(IBrokeredMessageWrapper azureServiceBusMessage)
        {
            var count = 0;
            if (azureServiceBusMessage.ApplicationProperties != null &&
                azureServiceBusMessage.ApplicationProperties.ContainsKey(ASBConstants.HandledCountHeaderBagKey))
            {
                int.TryParse(
                    azureServiceBusMessage.ApplicationProperties[ASBConstants.HandledCountHeaderBagKey].ToString(),
                    out count);
            }

            return count;
        }

        private void EnsureServiceBusEntityExists()
        {
            if (_subscriptionConfiguration.UseQueue)
            {
                EnsureQueue(_subscriptionConfiguration);
            }
            else
            {
                EnsureSubscription(_subscriptionConfiguration);
            }
        }

        private bool AsbEntityCreatedOrAssumed => _subscriptionCreated || _makeChannel.Equals(OnMissingChannel.Assume);

        private void EnsureQueue(AzureServiceBusSubscriptionConfiguration subscriptionConfiguration)
        {
            if (AsbEntityCreatedOrAssumed)
                return;

            try
            {
                if (_administrationClientWrapper.QueueExists(_queueOrTopicName))
                {
                    _subscriptionCreated = true;
                }

                if (_makeChannel.Equals(OnMissingChannel.Validate))
                {
                    throw new ChannelFailureException(
                        $"Queue {_queueOrTopicName} does not exist and missing channel mode set to Validate.");
                }

                _administrationClientWrapper.CreateQueue(_queueOrTopicName, _subscriptionConfiguration);
                _subscriptionCreated = true;
            }
            catch (ServiceBusException ex)
            {
                if (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
                {
                    s_logger.LogWarning(
                        "Message entity already exists with queue {Queue}.",
                        _queueOrTopicName);
                    _subscriptionCreated = true;
                }
                else
                {
                    throw new ChannelFailureException("Failing to check or create queue", ex);
                }
            }
        }

        private void EnsureSubscription(AzureServiceBusSubscriptionConfiguration subscriptionConfiguration)
        {
            if (AsbEntityCreatedOrAssumed)
                return;

            try
            {
                if (_administrationClientWrapper.SubscriptionExists(_queueOrTopicName, _subscriptionName))
                {
                    _subscriptionCreated = true;
                    return;
                }

                if (_makeChannel.Equals(OnMissingChannel.Validate))
                {
                    throw new ChannelFailureException(
                        $"Subscription {_subscriptionName} does not exist on topic {_queueOrTopicName} and missing channel mode set to Validate.");
                }

                _administrationClientWrapper.CreateSubscription(_queueOrTopicName, _subscriptionName,
                    subscriptionConfiguration);
                _subscriptionCreated = true;
            }
            catch (ServiceBusException ex)
            {
                if (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
                {
                    s_logger.LogWarning(
                        "Message entity already exists with topic {Topic} and subscription {ChannelName}.",
                        _queueOrTopicName,
                        _subscriptionName);
                    _subscriptionCreated = true;
                }
                else
                {
                    throw new ChannelFailureException("Failing to check or create subscription", ex);
                }
            }
            catch (Exception e)
            {
                s_logger.LogError(e, "Failing to check or create subscription.");

                //The connection to Azure Service bus may have failed so we re-establish the connection.
                _administrationClientWrapper.Reset();

                throw new ChannelFailureException("Failing to check or create subscription", e);
            }
        }

        private void HandleASBException(ServiceBusException ex, Guid messageId)
        {
            if (ex.Reason == ServiceBusFailureReason.MessageLockLost)
                s_logger.LogError(ex, "Error completing peak lock on message with id {Id}", messageId);
            else
            {
                s_logger.LogError(ex,
                    "Error completing peak lock on message with id {Id} Reason {ErrorReason}",
                    messageId, ex.Reason);
            }
        }
    }
}

using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus.Administration;

namespace Paramore.Brighter.MessagingGateway.AzureServiceBus.AzureServiceBusWrappers
{
    /// <summary>
    /// A wrapper for the Azure Service Bus Administration Client
    /// </summary>
    public interface IAdministrationClientWrapper
    {
        /// <summary>
        /// Check if a Topic exists
        /// </summary>
        /// <param name="topic">The name of the Topic.</param>
        /// <returns>True if the Topic exists.</returns>
        bool TopicExists(string topic);

        /// /// <summary>
        /// Create a Topic
        /// </summary>
        /// <param name="topic">The name of the Topic</param>
        void CreateTopic(string topic);

        /// <summary>
        /// Delete a Topic.
        /// </summary>
        /// <param name="topic">The name of the Topic.</param>
        Task DeleteTopicAsync(string topic);

        /// <summary>
        /// Check if a Subscription Exists for a Topic.
        /// </summary>
        /// <param name="topicName">The name of the Topic.</param>
        /// <param name="subscriptionName">The name of the Subscription</param>
        /// <returns>True if the subscription exists on the specified Topic.</returns>
        bool SubscriptionExists(string topicName, string subscriptionName);

        /// <summary>
        /// Create a Subscription.
        /// </summary>
        /// <param name="topicName">The name of the Topic.</param>
        /// <param name="subscriptionName">The name of the Subscription.</param>
        /// <param name="subscriptionConfiguration">The configuration options for the subscriptions.</param>
        void CreateSubscription(string topicName, string subscriptionName,
            AzureServiceBusSubscriptionConfiguration subscriptionConfiguration);

        /// <summary>
        /// Reset the Connection.
        /// </summary>
        void Reset();

        /// <summary>
        /// Get a Subscription.
        /// </summary>
        /// <param name="topicName">The name of the Topic.</param>
        /// <param name="subscriptionName">The name of the Subscription.</param>
        /// <param name="cancellationToken">The Cancellation Token.</param>
        Task<SubscriptionProperties> GetSubscriptionAsync(string topicName, string subscriptionName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if a Topic exists
        /// </summary>
        /// <param name="queue">The name of the queue.</param>
        /// <returns>True if the Queue exists.</returns>>
        bool QueueExists(string queue);

        /// /// <summary>
        /// Create a Queue
        /// </summary>
        /// <param name="queue">The name of the Queue</param>
        /// <param name="subscriptionConfiguration"></param>
        void CreateQueue(string queue, AzureServiceBusSubscriptionConfiguration subscriptionConfiguration);
    }
}

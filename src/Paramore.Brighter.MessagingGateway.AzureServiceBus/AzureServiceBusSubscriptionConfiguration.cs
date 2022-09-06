using System;
using System.Collections.Generic;

namespace Paramore.Brighter.MessagingGateway.AzureServiceBus
{
    public class AzureServiceBusSubscriptionConfiguration
    {
        /// <summary>
        /// The Maximum amount of times that a Message can be delivered before it is dead Lettered
        /// </summary>
        public int MaxDeliveryCount { get; set; } = 5;
        /// <summary>
        /// Dead letter a message when it expires
        /// </summary>
        public bool DeadLetteringOnMessageExpiration { get; set; } = true;
        /// <summary>
        /// How long message locks are held for
        /// </summary>
        public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(1);
        /// <summary>
        /// How long messages sit in the queue before they expire
        /// </summary>
        public TimeSpan DefaultMessageTimeToLive { get; set; } = TimeSpan.FromDays(3);
        /// <summary>
        /// A Sql Filter to apply to the subscription
        /// </summary>
        public string SqlFilter = string.Empty;
        /// <summary>
        /// Uses ASB Queue instead of ASB Subscription
        /// SqlFilter is Ignored when set to true
        /// </summary>
        public bool UseQueue { get; set; } = false;
    }
}

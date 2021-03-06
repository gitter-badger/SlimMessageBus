﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Common.Logging;
using SlimMessageBus.Host.Config;

namespace SlimMessageBus.Host.Memory
{
    /// <summary>
    /// In memory message bus <see cref="IMessageBus"/> implementation to use for in process message passing.
    /// </summary>
    public class MemoryMessageBus : MessageBusBase
    {
        private static readonly ILog Log = LogManager.GetLogger<MemoryMessageBus>();

        private MemoryMessageBusSettings ProviderSettings { get; }

        private readonly IDictionary<string, List<ConsumerRuntimeInfo>> _consumersByTopic;

        public MemoryMessageBus(MessageBusSettings settings, MemoryMessageBusSettings providerSettings) 
            : base(settings)
        {
            ProviderSettings = providerSettings;

            var consumers = settings.Consumers.Select(x => new ConsumerRuntimeInfo(x)).ToList();
            _consumersByTopic = consumers
                .GroupBy(x => x.ConsumerSettings.Topic)
                .ToDictionary(x => x.Key, x => x.ToList());
        }

        #region Overrides of MessageBusBase

        public override Task ProduceToTransport(Type messageType, object message, string name, byte[] payload)
        {
            if (!_consumersByTopic.TryGetValue(name, out var consumers))
            {
                Log.DebugFormat(CultureInfo.InvariantCulture, "No consumers interested in event {0} on topic {1}", messageType, name);
                return Task.CompletedTask;
            }

            var tasks = new LinkedList<Task>();
            foreach (var consumer in consumers)
            {
                // obtain the consumer from DI
                Log.DebugFormat(CultureInfo.InvariantCulture, "Resolving consumer type {0}", consumer.ConsumerSettings.ConsumerType);
                var consumerInstance = Settings.DependencyResolver.Resolve(consumer.ConsumerSettings.ConsumerType);
                if (consumerInstance == null)
                {
                    Log.WarnFormat(CultureInfo.InvariantCulture, "The dependency resolver did not yield any instance of {0}", consumer.ConsumerSettings.ConsumerType);
                    continue;
                }

                if (consumer.ConsumerSettings.IsRequestMessage)
                {
                    Log.Warn("The in memory provider only supports pub-sub communication for now");
                    continue;
                }

                var messageForConsumer = ProviderSettings.EnableMessageSerialization
                    ? DeserializeMessage(messageType, payload) // will pass a deep copy of the message
                    : message; // prevent deep copy of the message

                Log.DebugFormat(CultureInfo.InvariantCulture, "Invoking consumer {0}", consumerInstance.GetType());
                var task = consumer.OnHandle(consumerInstance, messageForConsumer);

                tasks.AddLast(task);
            }

            Log.DebugFormat(CultureInfo.InvariantCulture, "Waiting on {0} consumer tasks", tasks.Count);
            return Task.WhenAll(tasks);
        }

        public override byte[] SerializeMessage(Type messageType, object message)
        {
            if (ProviderSettings.EnableMessageSerialization)
            {
                return base.SerializeMessage(messageType, message);
            }
            // the serialized payload is not going to be used
            return null;
        }

        #endregion
    }
}

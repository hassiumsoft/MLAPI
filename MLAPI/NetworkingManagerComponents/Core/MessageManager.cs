﻿using MLAPI.MonoBehaviours.Core;
using MLAPI.NetworkingManagerComponents.Binary;
using System;
using System.Collections.Generic;

namespace MLAPI.NetworkingManagerComponents.Core
{
    internal static class MessageManager
    {
        internal static Dictionary<string, int> channels;
        internal static Dictionary<int, string> reverseChannels;
        internal static Dictionary<string, ushort> messageTypes;
        internal static Dictionary<ushort, string> reverseMessageTypes;
        
        internal static Dictionary<ushort, Dictionary<int, Action<uint, BitReader>>> messageCallbacks;
        internal static Dictionary<ushort, int> messageHandlerCounter;
        internal static Dictionary<ushort, Stack<int>> releasedMessageHandlerCounters;

        private static NetworkingManager netManager
        {
            get
            {
                return NetworkingManager.singleton;
            }
        }

        
        internal static int AddIncomingMessageHandler(string name, Action<uint, BitReader> action, uint networkId)
        {
            if (messageTypes.ContainsKey(name))
            {
                if (messageCallbacks.ContainsKey(messageTypes[name]))
                {
                    int handlerId = 0;
                    if (messageHandlerCounter.ContainsKey(messageTypes[name]))
                    {
                        if (!releasedMessageHandlerCounters.ContainsKey(messageTypes[name]))
                            releasedMessageHandlerCounters.Add(messageTypes[name], new Stack<int>());

                        if (releasedMessageHandlerCounters[messageTypes[name]].Count == 0)
                        {
                            handlerId = messageHandlerCounter[messageTypes[name]];
                            messageHandlerCounter[messageTypes[name]]++;
                        }
                        else
                        {
                            handlerId = releasedMessageHandlerCounters[messageTypes[name]].Pop();
                        }
                    }
                    else
                    {
                        messageHandlerCounter.Add(messageTypes[name], handlerId + 1);
                    }
                    messageCallbacks[messageTypes[name]].Add(handlerId, action);
                    return handlerId;
                }
                else
                {
                    messageCallbacks.Add(messageTypes[name], new Dictionary<int, Action<uint, BitReader>>());
                    messageHandlerCounter.Add(messageTypes[name], 1);
                    messageCallbacks[messageTypes[name]].Add(0, action);
                    return 0;
                }
            }
            else
            {
                if (LogHelper.CurrentLogLevel <= LogLevel.Normal) LogHelper.LogWarning("The message type " + name + " has not been registered. Please define it in the netConfig");
                return -1;
            }
        }

        internal static void RemoveIncomingMessageHandler(string name, int counter, uint networkId)
        {
            if (counter == -1)
                return;

            if (messageTypes.ContainsKey(name) && messageCallbacks.ContainsKey(messageTypes[name]) && messageCallbacks[messageTypes[name]].ContainsKey(counter))
            {
                messageCallbacks[messageTypes[name]].Remove(counter);
                if (!releasedMessageHandlerCounters.ContainsKey(messageTypes[name]))
                    releasedMessageHandlerCounters.Add(messageTypes[name], new Stack<int>());
                releasedMessageHandlerCounters[messageTypes[name]].Push(counter);
            }
        }
        
    }
}

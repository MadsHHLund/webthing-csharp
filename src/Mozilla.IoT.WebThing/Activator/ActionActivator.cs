using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Mozilla.IoT.WebThing.Activator
{
    internal sealed class ActionActivator : IActionActivator
    {
        private readonly Func<Type, ObjectFactory> _createFactory = type =>
            ActivatorUtilities.CreateFactory(type, Type.EmptyTypes);

        private readonly ConcurrentDictionary<Type, ObjectFactory>
            _typeActivatorCache = new ConcurrentDictionary<Type, ObjectFactory>();
        
        public Action CreateInstance(IServiceProvider serviceProvider, Thing thing, string name, IDictionary<string, object> input)
        {
            if (!thing.ActionsTypeInfo.ContainsKey(name))
            {
                return null;
            }

            var (type, _) = thing.ActionsTypeInfo[name];
            var action = CreateAction(serviceProvider, type);

            action.Thing = thing;
            action.Name = name;
            action.HrefPrefix = thing.HrefPrefix;
            action.Input = input;
            action.Href = $"/actions/{name}/{action.Id}";

            return action;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Action CreateAction(IServiceProvider serviceProvider, Type implementationType)
        {
            var createFactory = _typeActivatorCache.GetOrAdd(implementationType, _createFactory);
            return (Action)createFactory(serviceProvider, arguments: null);
        }
    }
}

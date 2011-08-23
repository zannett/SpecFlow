﻿/**************************************************************************************
 * 
 * MiniDi: A very simple IoC container, easily embeddable also as a source code. 
 * 
 * MiniDi was created to support SpecFlow (http://www.specflow.org) by Gaspar Nagy (http://gasparnagy.blogspot.com/)
 * 
 * Project source & unit tests: http://github.com/gasparnagy/MiniDi
 * License: Simplified BSD License
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED 
 * TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL 
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
 * CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
 * DEALINGS IN THE SOFTWARE.
 * 
 */
using System;
using System.Configuration;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;

namespace MiniDi
{
#if !MINIDI_LIMITEDRUNTIME
    [Serializable]
#endif
    public class ObjectContainerException : Exception
    {
        public ObjectContainerException(string message, IEnumerable<Type> resolutionPath) : base(GetMessage(message, resolutionPath))
        {
        }

#if !MINIDI_LIMITEDRUNTIME
        protected ObjectContainerException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
#endif

        static private string GetMessage(string message, IEnumerable<Type> resolutionPath)
        {
            if (resolutionPath == null || !resolutionPath.Any())
                return message;

            return string.Format("{0} (resolution path: {1})", message, string.Join("->", resolutionPath.Select(t => t.FullName).ToArray()));
        }
    }

    public interface IObjectContainer: IDisposable
    {
        /// <summary>
        /// Registeres a type as the desired implementation type of an interface.
        /// </summary>
        /// <typeparam name="TType">Implementation type</typeparam>
        /// <typeparam name="TInterface">Interface will be resolved</typeparam>
        /// <exception cref="ObjectContainerException">If there was already a resolve for the <typeparamref name="TInterface"/>.</exception>
        /// <remarks>
        ///     <para>Previous registrations can be overriden before the first resolution for the <typeparamref name="TInterface"/>.</para>
        /// </remarks>
        void RegisterTypeAs<TType, TInterface>() where TType : class, TInterface;
        /// <summary>
        /// Registers an instance 
        /// </summary>
        /// <typeparam name="TInterface">Interface will be resolved</typeparam>
        /// <param name="instance">The instance implements the interface.</param>
        /// <exception cref="ArgumentNullException">If <paramref name="instance"/> is null.</exception>
        /// <exception cref="ObjectContainerException">If there was already a resolve for the <typeparamref name="TInterface"/>.</exception>
        /// <remarks>
        ///     <para>Previous registrations can be overriden before the first resolution for the <typeparamref name="TInterface"/>.</para>
        ///     <para>The instance will be registered in the object pool, so if a <see cref="Resolve{T}"/> (for another interface) would require an instance of the dynamic type of the <paramref name="instance"/>, the <paramref name="instance"/> will be returned.</para>
        /// </remarks>
        void RegisterInstanceAs<TInterface>(TInterface instance) where TInterface : class;

        /// <summary>
        /// Registers an instance 
        /// </summary>
        /// <param name="instance">The instance implements the interface.</param>
        /// <param name="interfaceType">Interface will be resolved</param>
        /// <exception cref="ArgumentNullException">If <paramref name="instance"/> is null.</exception>
        /// <exception cref="ObjectContainerException">If there was already a resolve for the <paramref name="interfaceType"/>.</exception>
        /// <remarks>
        ///     <para>Previous registrations can be overriden before the first resolution for the <paramref name="interfaceType"/>.</para>
        ///     <para>The instance will be registered in the object pool, so if a <see cref="Resolve{T}"/> (for another interface) would require an instance of the dynamic type of the <paramref name="instance"/>, the <paramref name="instance"/> will be returned.</para>
        /// </remarks>
        void RegisterInstanceAs(object instance, Type interfaceType);

        /// <summary>
        /// Resolves an implementation object for an interface or type.
        /// </summary>
        /// <typeparam name="T">The interface or type.</typeparam>
        /// <returns>An object implementing <typeparamref name="T"/>.</returns>
        /// <remarks>
        ///     <para>The container pools the objects, so if the interface is resolved twice or the same type is registered for multiple interfaces, a single instance is created and returned.</para>
        /// </remarks>
        T Resolve<T>();

        /// <summary>
        /// Resolves an implementation object for an interface or type.
        /// </summary>
        /// <param name="typeToResolve">The interface or type.</param>
        /// <returns>An object implementing <paramref name="typeToResolve"/>.</returns>
        /// <remarks>
        ///     <para>The container pools the objects, so if the interface is resolved twice or the same type is registered for multiple interfaces, a single instance is created and returned.</para>
        /// </remarks>
        object Resolve(Type typeToResolve);
    }

    public interface IContainedInstance
    {
        IObjectContainer Container { get; }
    }

    public class ObjectContainer : IObjectContainer
    {
        private bool isDisposed = false;
        private readonly ObjectContainer baseContainer;
        private readonly Dictionary<Type, Type> typeRegistrations = new Dictionary<Type, Type>();
        private readonly Dictionary<Type, object> instanceRegistrations = new Dictionary<Type, object>();
        private readonly Dictionary<Type, object> resolvedObjects = new Dictionary<Type, object>();
        private readonly Dictionary<Type, object> objectPool = new Dictionary<Type, object>();

        public ObjectContainer()
        {
            RegisterInstanceAs<IObjectContainer>(this);
        }

        public ObjectContainer(IObjectContainer baseContainer) : this()
        {
            if (baseContainer != null && !(baseContainer is ObjectContainer))
                throw new ArgumentException("Base container must be an ObjectContainer", "baseContainer");

            this.baseContainer = (ObjectContainer)baseContainer;
        }

        #region Registration

        public void RegisterTypeAs<TInterface>(Type implementationType) where TInterface : class
        {
            Type interfaceType = typeof(TInterface);
            RegisterTypeAs(implementationType, interfaceType);
        }

        public void RegisterTypeAs<TType, TInterface>() where TType : class, TInterface
        {
            Type interfaceType = typeof(TInterface);
            Type implementationType = typeof(TType);
            RegisterTypeAs(implementationType, interfaceType);
        }

        private void RegisterTypeAs(Type implementationType, Type interfaceType)
        {
            AssertNotResolved(interfaceType);

            ClearRegistrations(interfaceType);
            typeRegistrations[interfaceType] = implementationType;
        }

        public void RegisterInstanceAs(object instance, Type interfaceType)
        {
            if (instance == null)
                throw new ArgumentNullException("instance");
            AssertNotResolved(interfaceType);

            ClearRegistrations(interfaceType);
            instanceRegistrations[interfaceType] = instance;
            objectPool[instance.GetType()] = instance;
        }

        public void RegisterInstanceAs<TInterface>(TInterface instance) where TInterface : class
        {
            RegisterInstanceAs(instance, typeof(TInterface));
        }

        private void AssertNotResolved(Type interfaceType)
        {
            if (resolvedObjects.ContainsKey(interfaceType))
                throw new ObjectContainerException("An object have been resolved for this interface already.", null);
        }

        private void ClearRegistrations(Type interfaceType)
        {
            typeRegistrations.Remove(interfaceType);
            instanceRegistrations.Remove(interfaceType);
        }

#if !MINIDI_LIMITEDRUNTIME
        public void RegisterFromConfiguration()
        {
            var section = (MiniDiConfigurationSection)ConfigurationManager.GetSection("miniDi");
            if (section == null)
                return;

            RegisterFromConfiguration(section.Registrations);
        }

        public void RegisterFromConfiguration(ContainerRegistrationCollection containerRegistrationCollection)
        {
            if (containerRegistrationCollection == null)
                return;

            foreach (ContainerRegistrationConfigElement registrationConfigElement in containerRegistrationCollection)
            {
                RegisterFromConfiguration(registrationConfigElement);
            }
        }

        private void RegisterFromConfiguration(ContainerRegistrationConfigElement registrationConfigElement)
        {
            Type interfaceType = Type.GetType(registrationConfigElement.Interface, true);
            Type implementationType = Type.GetType(registrationConfigElement.Implementation, true);

            RegisterTypeAs(implementationType, interfaceType);
        }
#endif

        #endregion

        #region Resolve

        public T Resolve<T>()
        {
            Type typeToResolve = typeof(T);

            object resolvedObject = Resolve(typeToResolve);

            return (T)resolvedObject;
        }

        public object Resolve(Type typeToResolve)
        {
            return Resolve(typeToResolve, Enumerable.Empty<Type>());
        }

        private object Resolve(Type typeToResolve, IEnumerable<Type> resolutionPath)
        {
            AssertNotDisposed();

            object resolvedObject;
            if (!resolvedObjects.TryGetValue(typeToResolve, out resolvedObject))
            {
                resolvedObject = CreateObjectFor(typeToResolve, resolutionPath);
                resolvedObjects.Add(typeToResolve, resolvedObject);
            }
            Debug.Assert(typeToResolve.IsInstanceOfType(resolvedObject));
            return resolvedObject;
        }

        private class RegistrationResult
        {
            public readonly object RegisteredInstance;
            public readonly Type RegisteredType;

            public bool IsTypeRegistration { get { return RegisteredType != null; } }
            public bool IsInstanceRegistration { get { return !IsTypeRegistration; } }

            public RegistrationResult(object registeredInstance)
            {
                RegisteredInstance = registeredInstance;
                RegisteredType = null;
            }

            public RegistrationResult(Type registeredType)
            {
                RegisteredType = registeredType;
                RegisteredInstance = null;
            }
        }

        private RegistrationResult GetRegistrationResult(Type typeToResolve)
        {
            object obj;
            if (instanceRegistrations.TryGetValue(typeToResolve, out obj))
            {
                return new RegistrationResult(obj);
            }

            Type registeredType;
            if (typeRegistrations.TryGetValue(typeToResolve, out registeredType))
            {
                return new RegistrationResult(registeredType);
            }

            if (baseContainer != null)
                return baseContainer.GetRegistrationResult(typeToResolve);

            return null;
        }

        private object GetPooledObject(Type registeredType)
        {
            object obj;
            if (objectPool.TryGetValue(registeredType, out obj))
                return obj;

            if (baseContainer != null)
                return baseContainer.GetPooledObject(registeredType);

            return null;
        }

        private object CreateObjectFor(Type typeToResolve, IEnumerable<Type> resolutionPath)
        {
            if (typeToResolve.IsPrimitive || typeToResolve == typeof(string))
                throw new ObjectContainerException("Primitive types cannot be resolved: " + typeToResolve.FullName, resolutionPath);

            var registrationResult = GetRegistrationResult(typeToResolve);

            if (registrationResult != null && registrationResult.IsInstanceRegistration)
            {
                return registrationResult.RegisteredInstance;
            }

            Type registeredType = typeToResolve;
            if (registrationResult != null)
                registeredType = registrationResult.RegisteredType;

            object obj = GetPooledObject(registeredType);

            if (obj == null)
            {
                if (registeredType.IsInterface)
                    throw new ObjectContainerException("Interface cannot be resolved: " + typeToResolve.FullName, resolutionPath);

                obj = CreateObject(registeredType, resolutionPath);
                objectPool.Add(registeredType, obj);
            }

            return obj;
        }

        private object CreateObject(Type type, IEnumerable<Type> resolutionPath)
        {
            var ctors = type.GetConstructors();
            if (ctors.Length == 0)
                ctors = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);

            if (ctors.Length == 0)
            {
                throw new ObjectContainerException("Class must have a constructor! " + type.FullName, resolutionPath);
            }

            int maxParamCount = ctors.Max(ctor => ctor.GetParameters().Length);
            var maxParamCountCtors = ctors.Where(ctor => ctor.GetParameters().Length == maxParamCount).ToArray();

            object obj;
            if (maxParamCountCtors.Length == 1)
            {
                ConstructorInfo ctor = maxParamCountCtors[0];
                if (resolutionPath.Contains(type))
                    throw new ObjectContainerException("Circular dependency found! " + type.FullName, resolutionPath);

                var args = ResolveArguments(ctor.GetParameters(), resolutionPath.Concat(new[] { type }));
                obj = ctor.Invoke(args);
            }
            else
            {
                throw new ObjectContainerException("Multiple public constructors with same maximum parameter count are not supported! " + type.FullName, resolutionPath);
            }

            return obj;
        }

        private object[] ResolveArguments(IEnumerable<ParameterInfo> parameters, IEnumerable<Type> resolutionPath)
        {
            return parameters.Select(p => Resolve(p.ParameterType, resolutionPath)).ToArray();
        }

        #endregion

        private void AssertNotDisposed()
        {
            if (isDisposed)
                throw new ObjectContainerException("Object container disposed", null);
        }

        public void Dispose()
        {
            isDisposed = true;

            foreach (var obj in objectPool.Values.OfType<IDisposable>().Where(o => !ReferenceEquals(o, this)))
                obj.Dispose();

            objectPool.Clear();
            instanceRegistrations.Clear();
            typeRegistrations.Clear();
            resolvedObjects.Clear();
        }
    }

    #region Configuration handling
#if !MINIDI_LIMITEDRUNTIME

    public class MiniDiConfigurationSection : ConfigurationSection
    {
        [ConfigurationProperty("", Options = ConfigurationPropertyOptions.IsDefaultCollection)]
        [ConfigurationCollection(typeof(ContainerRegistrationCollection), AddItemName = "register")]
        public ContainerRegistrationCollection Registrations
        {
            get { return (ContainerRegistrationCollection)this[""]; }
            set { this[""] = value; }
        }
    }

    public class ContainerRegistrationCollection : ConfigurationElementCollection
    {
        protected override ConfigurationElement CreateNewElement()
        {
            return new ContainerRegistrationConfigElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((ContainerRegistrationConfigElement)element).Interface;
        }
    }

    public class ContainerRegistrationConfigElement : ConfigurationElement
    {
        [ConfigurationProperty("as", IsRequired = true)]
        public string Interface
        {
            get { return (string)this["as"]; }
            set { this["as"] = value; }
        }

        [ConfigurationProperty("type", IsRequired = true)]
        public string Implementation
        {
            get { return (string)this["type"]; }
            set { this["type"] = value; }
        }
    }

#endif
    #endregion
}
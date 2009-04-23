// Copyright 2007-2008 The Apache Software Foundation.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use 
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed 
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit.Tests.Serialization.Approach
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Linq;
	using System.Reflection;
	using System.Xml;
	using log4net;
	using Magnum.CollectionExtensions;
	using Magnum.Monads;
	using Magnum.Reflection;

	public class SerializerContext :
		ISerializerContext
	{
		private static readonly ILog _log = LogManager.GetLogger(typeof (SerializerContext));

		private static readonly Dictionary<Type, IObjectSerializer> _serializers;

		private static Dictionary<Type, IObjectFieldCache> _fieldCaches;
		private static Dictionary<Type, IObjectPropertyCache> _propertyCaches;
		private NamespaceTable _namespaceTable = new NamespaceTable();

		static SerializerContext()
		{
			_propertyCaches = new Dictionary<Type, IObjectPropertyCache>();
			_fieldCaches = new Dictionary<Type, IObjectFieldCache>();
			_serializers = new Dictionary<Type, IObjectSerializer>();

			LoadBuiltInSerializers();
		}

		public string GetPrefix(string localName, string ns)
		{
			return _namespaceTable.GetPrefix(localName, ns);
		}

		public void WriteNamespaceInformationToXml(XmlWriter writer)
		{
			_namespaceTable.Each((ns, prefix) => writer.WriteAttributeString("xmlns", prefix, null, ns));
		}


		public IEnumerable<K<Action<XmlWriter>>> SerializeObject(string localName, Type type, object value)
		{
			IObjectSerializer serializer = GetSerializerFor(type);

			foreach (var action in serializer.GetSerializationActions(this, localName, value))
			{
				yield return action;
			}
		}

		public IEnumerable<K<Action<XmlWriter>>> Serialize(object value)
		{
			if (value == null)
				yield break;

			Type type = value.GetType();
			string localName = type.ToXmlFriendlyName();

			foreach (var action in SerializeObject(localName, type, value))
			{
				yield return action;
			}
		}

		private IObjectSerializer GetSerializerFor(Type type)
		{
			return _serializers.Retrieve(type, () => CreateSerializerFor(type));
		}

		private IObjectSerializer CreateSerializerFor(Type type)
		{
			if (typeof (IEnumerable).IsAssignableFrom(type))
			{
				return CreateEnumerableSerializerFor(type);
			}

			Type serializerType = typeof (ObjectSerializer<>).MakeGenericType(type);

			var propertyCache = _propertyCaches.Retrieve(type, () => new ObjectPropertyCache(type));
			var fieldCache = _fieldCaches.Retrieve(type, () => new ObjectFieldCache(type));

			var serializer = (IObjectSerializer) Activator.CreateInstance(serializerType, propertyCache, fieldCache);

			return serializer;
		}

		private IObjectSerializer CreateEnumerableSerializerFor(Type type)
		{
			Type[] genericArguments = type.GetDeclaredGenericArguments().ToArray();
			if (genericArguments == null || genericArguments.Length == 0)
			{
				Type elementType = type.IsArray ? type.GetElementType() : typeof (object);

				Type serializerType = typeof (ArraySerializer<>).MakeGenericType(elementType);

				return (IObjectSerializer)ClassFactory.New(serializerType);
			}

			if (type.ImplementsGeneric(typeof(IDictionary<,>)))
			{
				Type serializerType = typeof (DictionarySerializer<,>).MakeGenericType(genericArguments);

				return (IObjectSerializer)ClassFactory.New(serializerType);
			}

			if (type.ImplementsGeneric(typeof (IList<>)) || type.ImplementsGeneric(typeof (IEnumerable<>)))
			{
				Type serializerType = typeof (ListSerializer<>).MakeGenericType(genericArguments[0]);

				return (IObjectSerializer)ClassFactory.New(serializerType);
			}

			throw new InvalidOperationException("enumerations not yet supported");
		}

		private static void LoadBuiltInSerializers()
		{
			var query = Assembly.GetExecutingAssembly()
				.GetTypes()
				.Where(x => x.BaseType != null && x.BaseType.IsGenericType && x.BaseType.GetGenericTypeDefinition() == typeof (SerializerBase<>));

			foreach (Type type in query)
			{
				Type itemType = type.BaseType.GetGenericArguments()[0];

				_log.DebugFormat("Adding serializer for {0} ({1})", itemType.Name, type.Name);

				_serializers.Add(itemType, ClassFactory.New(type) as IObjectSerializer);
			}
		}
	}


	public static class UsefulExtensionMethods
	{
		public static bool ImplementsGeneric(this Type type, Type targetType)
		{
			if (type.IsGenericType && type.GetGenericTypeDefinition() == targetType)
				return true;

			var count = type.GetInterfaces()
				.Where(x => x.IsGenericType)
				.Where(x => x.GetGenericTypeDefinition() == targetType)
				.Count();

			if (count > 0)
				return true;

			var baseType = type.BaseType;
			if (baseType == null)
				return false;

			return baseType.IsGenericType
					? baseType.GetGenericTypeDefinition() == targetType
					: baseType.ImplementsGeneric(targetType);
		}

		public static string ToXmlFriendlyName(this Type type)
		{
			string name = type.Name;

			if (type.IsGenericType)
			{
				int index = name.IndexOf('`');
				if (index > 0)
					return name.Substring(0, index);

			}

			return name;
		}
	}
}
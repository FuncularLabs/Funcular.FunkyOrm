using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.Exceptions;

namespace Funcular.Data.Orm.PostgreSql
{
    public class RemotePathResolver
    {
        private static readonly ConcurrentDictionary<Type, List<PropertyInfo>> _foreignKeysCache = new ConcurrentDictionary<Type, List<PropertyInfo>>();
        private static readonly ConcurrentDictionary<Type, List<Tuple<Type, PropertyInfo>>> _incomingForeignKeysCache = new ConcurrentDictionary<Type, List<Tuple<Type, PropertyInfo>>>();
        private static readonly ConcurrentDictionary<string, ResolvedRemotePath> _resolvedPaths = new ConcurrentDictionary<string, ResolvedRemotePath>();
        private static readonly ConcurrentDictionary<Assembly, List<Type>> _assemblyTypesCache = new ConcurrentDictionary<Assembly, List<Type>>();

        public ResolvedRemotePath Resolve(Type sourceType, Type remoteType, string[] keyPath)
        {
            string cacheKey = $"{sourceType.FullName}|{remoteType.FullName}|{string.Join(",", keyPath)}";
            return _resolvedPaths.GetOrAdd(cacheKey, _ => ResolveInternal(sourceType, remoteType, keyPath));
        }

        private ResolvedRemotePath ResolveInternal(Type sourceType, Type remoteType, string[] keyPath)
        {
            if (keyPath.Length == 1)
                return ResolveImplicit(sourceType, remoteType, keyPath[0]);
            else
                return ResolveExplicit(sourceType, remoteType, keyPath);
        }

        private ResolvedRemotePath ResolveImplicit(Type sourceType, Type remoteType, string targetPropertyName)
        {
            var queue = new Queue<PathNode>();
            queue.Enqueue(new PathNode { CurrentType = sourceType, Path = new List<JoinStep>() });

            var visited = new Dictionary<Type, int>();
            visited[sourceType] = 0;

            var validPaths = new List<ResolvedRemotePath>();
            int minLength = int.MaxValue;

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node.Path.Count > minLength) continue;

                if (node.CurrentType == remoteType)
                {
                    var targetProp = remoteType.GetProperty(targetPropertyName);
                    if (targetProp != null)
                    {
                        var resolved = new ResolvedRemotePath
                        {
                            Joins = node.Path,
                            FinalColumnName = targetPropertyName,
                            TargetProperty = targetProp
                        };
                        if (node.Path.Count < minLength) { minLength = node.Path.Count; validPaths.Clear(); validPaths.Add(resolved); }
                        else if (node.Path.Count == minLength) { validPaths.Add(resolved); }
                    }
                    continue;
                }

                var fks = GetForeignKeys(node.CurrentType);
                foreach (var fk in fks)
                {
                    var targetType = GetForeignKeyTarget(fk);
                    if (targetType == null) continue;
                    int newDepth = node.Path.Count + 1;
                    if (!visited.ContainsKey(targetType) || visited[targetType] >= newDepth)
                    {
                        visited[targetType] = newDepth;
                        var newPath = new List<JoinStep>(node.Path);
                        newPath.Add(new JoinStep { SourceTableType = node.CurrentType, TargetTableType = targetType, ForeignKeyProperty = fk.Name, TargetKeyProperty = "Id", IsReverse = false });
                        queue.Enqueue(new PathNode { CurrentType = targetType, Path = newPath });
                    }
                }

                var incomingFks = GetIncomingForeignKeys(node.CurrentType);
                foreach (var incoming in incomingFks)
                {
                    var sourceTableType = incoming.Item1;
                    var fkProp = incoming.Item2;
                    int newDepth = node.Path.Count + 1;
                    if (!visited.ContainsKey(sourceTableType) || visited[sourceTableType] >= newDepth)
                    {
                        visited[sourceTableType] = newDepth;
                        var newPath = new List<JoinStep>(node.Path);
                        newPath.Add(new JoinStep { SourceTableType = node.CurrentType, TargetTableType = sourceTableType, ForeignKeyProperty = "Id", TargetKeyProperty = fkProp.Name, IsReverse = true });
                        queue.Enqueue(new PathNode { CurrentType = sourceTableType, Path = newPath });
                    }
                }
            }

            if (validPaths.Count == 0)
                throw new PathNotFoundException($"No path found from {sourceType.Name} to {remoteType.Name}");

            if (validPaths.Count > 1)
            {
                var distinctPaths = validPaths.Select(p => string.Join("->", p.Joins.Select(j => j.ForeignKeyProperty))).Distinct().ToList();
                if (distinctPaths.Count > 1)
                    throw new Funcular.Data.Orm.Exceptions.AmbiguousMatchException($"Multiple paths found from {sourceType.Name} to {remoteType.Name}: {string.Join(", ", distinctPaths)}");
                return validPaths.OrderByDescending(p => CalculatePathScore(p, sourceType, remoteType)).First();
            }

            return validPaths[0];
        }

        private int CalculatePathScore(ResolvedRemotePath path, Type sourceType, Type remoteType)
        {
            int score = 0;
            var targetNamespaces = new HashSet<string> { sourceType.Namespace, remoteType.Namespace };
            foreach (var join in path.Joins)
            {
                if (join.SourceTableType.GetCustomAttribute<TableAttribute>() != null) score += 10;
                if (join.TargetTableType.GetCustomAttribute<TableAttribute>() != null) score += 10;
                if (targetNamespaces.Contains(join.SourceTableType.Namespace)) score += 1;
                if (targetNamespaces.Contains(join.TargetTableType.Namespace)) score += 1;
            }
            return score;
        }

        private ResolvedRemotePath ResolveExplicit(Type sourceType, Type remoteType, string[] keyPath)
        {
            var joins = new List<JoinStep>();
            var currentType = sourceType;

            for (int i = 0; i < keyPath.Length - 1; i++)
            {
                string fkName = keyPath[i];
                var fkProp = currentType.GetProperty(fkName);
                if (fkProp != null)
                {
                    var targetType = GetForeignKeyTarget(fkProp);
                    if (targetType == null)
                        throw new PathNotFoundException($"Could not determine target type for FK {fkName} on {currentType.Name}");
                    joins.Add(new JoinStep { SourceTableType = currentType, TargetTableType = targetType, ForeignKeyProperty = fkName, TargetKeyProperty = "Id", IsReverse = false });
                    currentType = targetType;
                }
                else
                {
                    var incoming = GetIncomingForeignKeys(currentType);
                    var match = incoming.FirstOrDefault(t => t.Item2.Name.Equals(fkName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        var targetType = match.Item1;
                        joins.Add(new JoinStep { SourceTableType = currentType, TargetTableType = targetType, ForeignKeyProperty = match.Item2.Name, TargetKeyProperty = "Id", IsReverse = true });
                        currentType = targetType;
                    }
                    else
                    {
                        throw new PathNotFoundException($"Property {fkName} not found on {currentType.Name} and no incoming FK found matching {fkName}.");
                    }
                }
            }

            if (currentType != remoteType)
            {
                var currentName = currentType.Name;
                var remoteName = remoteType.Name;
                string hint = "";
                if (currentName == remoteName && currentType != remoteType)
                {
                    hint = $"\n\nHINT: You have two different classes named '{currentName}'.\n" +
                           $"1. {currentType.FullName} (in {currentType.Assembly.GetName().Name})\n" +
                           $"2. {remoteType.FullName} (in {remoteType.Assembly.GetName().Name})";
                }
                throw new PathNotFoundException($"Explicit path ended at {currentType.FullName}, expected {remoteType.FullName}.{hint}");
            }

            var targetPropName = keyPath.Last();
            var targetProp = remoteType.GetProperty(targetPropName);
            if (targetProp == null)
                throw new PathNotFoundException($"Property {targetPropName} not found on {remoteType.Name}");

            return new ResolvedRemotePath { Joins = joins, FinalColumnName = targetPropName, TargetProperty = targetProp };
        }

        private IEnumerable<Tuple<Type, PropertyInfo>> GetIncomingForeignKeys(Type targetType)
        {
            return _incomingForeignKeysCache.GetOrAdd(targetType, t =>
            {
                var assembly = t.Assembly;
                var allTypes = GetAssemblyTypes(assembly);
                var incoming = new List<Tuple<Type, PropertyInfo>>();
                foreach (var type in allTypes)
                {
                    var fks = GetForeignKeys(type);
                    foreach (var fk in fks)
                    {
                        var target = GetForeignKeyTarget(fk);
                        if (target != null && (target == t || target.IsAssignableFrom(t)))
                            incoming.Add(Tuple.Create(type, fk));
                    }
                }
                return incoming;
            });
        }

        private IEnumerable<PropertyInfo> GetForeignKeys(Type type)
        {
            return _foreignKeysCache.GetOrAdd(type, t =>
                t.GetProperties().Where(p => IsForeignKey(p)).ToList());
        }

        private bool IsForeignKey(PropertyInfo p)
        {
            if (Attribute.IsDefined(p, typeof(RemoteAttributeBase))) return false;
            if (p.GetCustomAttribute<RemoteLinkAttribute>() != null) return true;
            if (p.Name.EndsWith("Id") && p.Name.Length > 2 && (p.PropertyType == typeof(int) || p.PropertyType == typeof(int?))) return true;
            return false;
        }

        private Type GetForeignKeyTarget(PropertyInfo p)
        {
            var attr = p.GetCustomAttribute<RemoteLinkAttribute>();
            if (attr != null) return attr.TargetType;
            string name = p.Name.Substring(0, p.Name.Length - 2);
            var types = GetAssemblyTypes(p.DeclaringType.Assembly);
            var exactMatches = types.Where(t => t.Name == name).ToList();
            Type type = null;
            if (exactMatches.Count == 1) type = exactMatches[0];
            else if (exactMatches.Count > 1) type = exactMatches.FirstOrDefault(t => t.Namespace == p.DeclaringType.Namespace) ?? exactMatches[0];
            if (type == null)
            {
                var candidates = types.Where(t => IsSuffixMatch(name, t.Name)).ToList();
                if (candidates.Count > 0) type = candidates.OrderByDescending(t => t.Name.Length).First();
            }
            return type;
        }

        private List<Type> GetAssemblyTypes(Assembly assembly)
        {
            return _assemblyTypesCache.GetOrAdd(assembly, a => a.GetTypes().Where(t => t.IsClass && !t.IsAbstract).ToList());
        }

        private bool IsSuffixMatch(string propertyNameBase, string typeName)
        {
            if (propertyNameBase.EndsWith(typeName)) return true;
            if (typeName.EndsWith("Entity"))
            {
                string shortName = typeName.Substring(0, typeName.Length - "Entity".Length);
                if (shortName.Length > 0 && propertyNameBase.EndsWith(shortName)) return true;
            }
            return false;
        }

        private class PathNode
        {
            public Type CurrentType { get; set; }
            public List<JoinStep> Path { get; set; }
        }
    }

    public class ResolvedRemotePath
    {
        public List<JoinStep> Joins { get; set; }
        public string FinalColumnName { get; set; }
        public PropertyInfo TargetProperty { get; set; }
    }

    public class JoinStep
    {
        public Type SourceTableType { get; set; }
        public Type TargetTableType { get; set; }
        public string ForeignKeyProperty { get; set; }
        public string TargetKeyProperty { get; set; }
        public bool IsReverse { get; set; }
    }
}

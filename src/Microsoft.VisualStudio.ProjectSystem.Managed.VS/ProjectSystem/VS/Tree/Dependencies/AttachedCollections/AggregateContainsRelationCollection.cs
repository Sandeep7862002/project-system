﻿// Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies.AttachedCollections
{
    /// <summary>
    /// An observable collection that aggregates child items of a given parent <see cref="IRelatableItem"/>
    /// across all of its <see cref="IRelation"/>s. Supports lazy materialization of child items.
    /// </summary>
    public sealed class AggregateContainsRelationCollection : IAggregateRelationCollection, INotifyCollectionChanged
    {
        /// <summary>
        /// Attempts to create a collection for the children of <paramref name="parentItem"/>.
        /// Fails only when no relations exist to produce child items for the given item's type.
        /// </summary>
        public static bool TryCreate(IRelatableItem parentItem, IRelationProvider relationProvider, [NotNullWhen(returnValue: true)] out AggregateContainsRelationCollection? collection)
        {
            ImmutableArray<IRelation> containsRelations = relationProvider.GetContainsRelationsFor(parentItem.GetType());

            if (containsRelations.IsEmpty)
            {
                collection = null;
                return false;
            }

            collection = new AggregateContainsRelationCollection(parentItem, containsRelations);
            return true;
        }

        private event NotifyCollectionChangedEventHandler? CollectionChanged;

        private readonly AggregateContainsRelationCollectionSpan[] _spans;
        private readonly IRelatableItem _item;
        private bool _isMaterialized;

        private AggregateContainsRelationCollection(IRelatableItem item, ImmutableArray<IRelation> containsRelations)
        {
            _item = item;
            _spans = containsRelations
                .Select(relation => new AggregateContainsRelationCollectionSpan(this, relation))
                .ToArray();
        }

        /// <summary>
        /// Called when the parent item's state is updated in order to propagate those state changes
        /// across relations to any materialized items, recursively.
        /// </summary>
        public void OnStateUpdated()
        {
            if (_isMaterialized)
            {
                int baseIndex = 0;
                foreach (AggregateContainsRelationCollectionSpan span in _spans)
                {
                    span.BaseIndex = baseIndex;
                    span.Relation.UpdateContainsCollection(parent: _item, span);
                    baseIndex += span.Items?.Count ?? 0;
                }
            }
        }

        event NotifyCollectionChangedEventHandler? INotifyCollectionChanged.CollectionChanged
        {
            add => CollectionChanged += value;
            remove => CollectionChanged -= value;
        }

        bool IAggregateRelationCollection.HasItems
            => _isMaterialized
                ? _spans.Any(span => span.Items?.Count > 0)
                : _spans.Any(span => span.Relation.HasContainedItem(_item));

        int ICollection.Count => _spans.Sum(span => span.Items?.Count ?? 0);

        void IAggregateRelationCollection.EnsureMaterialized()
        {
            if (!_isMaterialized)
            {
                // Set this to true first, as events raised during materialization may trigger calls back
                // into this object to read partially constructed state which is valid and must be supported.
                _isMaterialized = true;

                OnStateUpdated();
            }
        }

        internal void RaiseChange(NotifyCollectionChangedEventArgs e) => CollectionChanged?.Invoke(this, e);

        IEnumerator IEnumerable.GetEnumerator()
        {
            foreach (AggregateContainsRelationCollectionSpan span in _spans)
            {
                if (span.Items != null)
                {
                    foreach (IRelatableItem item in span.Items)
                    {
                        yield return item;
                    }
                }
            }
        }

        object IList.this[int index]
        {
            get
            {
                if (_isMaterialized)
                {
                    foreach (AggregateContainsRelationCollectionSpan span in _spans)
                    {
                        if (span.Items == null)
                        {
                            continue;
                        }

                        int spanCount = span.Items.Count;

                        if (spanCount == 0)
                        {
                            continue;
                        }

                        if (index < spanCount)
                        {
                            return span.Items[index];
                        }

                        index -= spanCount;
                    }
                }

                throw new ArgumentOutOfRangeException(nameof(index), index, "Invalid index.");
            }
            set => throw new NotSupportedException();
        }

        int IList.IndexOf(object value)
        {
            if (_isMaterialized && value is IRelatableItem item)
            {
                int baseIndex = 0;

                foreach (AggregateContainsRelationCollectionSpan span in _spans)
                {
                    if (span.Items == null)
                    {
                        continue;
                    }

                    int spanCount = span.Items.Count;

                    if (spanCount == 0)
                    {
                        continue;
                    }

                    int index = span.Items.IndexOf(item);

                    if (index != -1)
                    {
                        return baseIndex + index;
                    }

                    baseIndex += spanCount;
                }
            }

            return -1;
        }

        bool IList.Contains(object value) => value is IRelatableItem item && _spans.Any(span => span.Items?.Contains(item) == true);

        void ICollection.CopyTo(Array array, int index) => throw new NotSupportedException();
        object ICollection.SyncRoot => throw new NotSupportedException();
        bool ICollection.IsSynchronized => false;
        int IList.Add(object value) => throw new NotSupportedException();
        void IList.Clear() => throw new NotSupportedException();
        void IList.Insert(int index, object value) => throw new NotSupportedException();
        void IList.Remove(object value) => throw new NotSupportedException();
        void IList.RemoveAt(int index) => throw new NotSupportedException();
        bool IList.IsReadOnly => true;
        bool IList.IsFixedSize => true;
    }
}

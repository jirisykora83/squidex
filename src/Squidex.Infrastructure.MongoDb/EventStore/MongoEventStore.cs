﻿// ==========================================================================
//  MongoEventStore.cs
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex Group
//  All rights reserved.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Squidex.Infrastructure.CQRS.Events;
using Squidex.Infrastructure.Reflection;

// ReSharper disable ClassNeverInstantiated.Local
// ReSharper disable UnusedMember.Local
// ReSharper disable InvertIf

namespace Squidex.Infrastructure.MongoDb.EventStore
{
    public class MongoEventStore : MongoRepositoryBase<MongoEventCommit>, IEventStore, IExternalSystem
    {
        private sealed class EventCountEntity
        {
            [BsonId]
            [BsonElement]
            [BsonRepresentation(BsonType.String)]
            public Guid Id { get; set; }

            [BsonElement]
            [BsonRequired]
            public int EventCount { get; set; }
        }

        public MongoEventStore(IMongoDatabase database) 
            : base(database)
        {
        }

        protected override string CollectionName()
        {
            return "Events";
        }

        protected override Task SetupCollectionAsync(IMongoCollection<MongoEventCommit> collection)
        {
            return collection.Indexes.CreateOneAsync(IndexKeys.Ascending(x => x.EventStream).Ascending(x => x.EventsVersion), new CreateIndexOptions { Unique = true });
        }

        public void CheckConnection()
        {
            try
            {
                Database.ListCollections();
            }
            catch (Exception e)
            {
                throw new ConfigurationException($"MongoDb Event Store failed to connect to database {Database.DatabaseNamespace.DatabaseName}", e);
            }
        }

        public IObservable<EventData> GetEventsAsync(string streamName)
        {
            Guard.NotNullOrEmpty(streamName, nameof(streamName));

            return Observable.Create<EventData>(async (observer, ct) =>
            {
                try
                {
                    await Collection.Find(x => x.EventStream == streamName).ForEachAsync(commit =>
                    {
                        foreach (var @event in commit.Events)
                        {
                            var eventData = SimpleMapper.Map(@event, new EventData());

                            observer.OnNext(eventData);
                        }
                    }, ct);

                    observer.OnCompleted();
                }
                catch (Exception e)
                {
                    observer.OnError(e);
                }
            });
        }

        public IObservable<EventData> GetEventsAsync()
        {
            return Observable.Create<EventData>(async (observer, ct) =>
            {
                try
                {
                    await Collection.Find(new BsonDocument()).ForEachAsync(commit =>
                    {
                        foreach (var @event in commit.Events)
                        {
                            var eventData = SimpleMapper.Map(@event, new EventData());

                            observer.OnNext(eventData);
                        }
                    }, ct);

                    observer.OnCompleted();
                }
                catch (Exception e)
                {
                    observer.OnError(e);
                }
            });
        }

        public async Task AppendEventsAsync(Guid commitId, string streamName, int expectedVersion, IEnumerable<EventData> events)
        {
            var currentVersion = await GetEventVersionAsync(streamName);

            if (currentVersion != expectedVersion)
            {
                throw new WrongEventVersionException(currentVersion, expectedVersion);
            }

            var now = DateTime.UtcNow;

            var commit = new MongoEventCommit
            {
                Id = commitId,
                Events = events.Select(x => SimpleMapper.Map(x, new MongoEvent())).ToList(),
                EventStream = streamName,
                EventsVersion = expectedVersion,
                Timestamp = now
            };

            if (commit.Events.Any())
            {
                commit.EventCount = commit.Events.Count;

                try
                {
                    await Collection.InsertOneAsync(commit);
                }
                catch (MongoWriteException e)
                {
                    if (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                    {
                        currentVersion = await GetEventVersionAsync(streamName);

                        if (currentVersion != expectedVersion)
                        {
                            throw new WrongEventVersionException(currentVersion, expectedVersion);
                        }
                    }

                    throw;
                }
            }
        }

        private async Task<int> GetEventVersionAsync(string streamName)
        {
            var allCommits =
                await Collection.Find(c => c.EventStream == streamName)
                    .Project<BsonDocument>(Projection.Include(x => x.EventCount))
                    .ToListAsync();

            var currentVersion = allCommits.Sum(x => x["EventCount"].ToInt32()) - 1;

            return currentVersion;
        }
    }
}

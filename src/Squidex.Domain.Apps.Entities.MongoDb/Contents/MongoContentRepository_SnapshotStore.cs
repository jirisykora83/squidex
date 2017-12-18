﻿// ==========================================================================
//  MongoContentRepository_SnapshotStore.cs
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex Group
//  All rights reserved.
// ==========================================================================

using System;
using System.Threading.Tasks;
using MongoDB.Driver;
using Squidex.Domain.Apps.Core.ConvertContent;
using Squidex.Domain.Apps.Entities.Contents.State;
using Squidex.Infrastructure;
using Squidex.Infrastructure.MongoDb;
using Squidex.Infrastructure.Reflection;
using Squidex.Infrastructure.States;

namespace Squidex.Domain.Apps.Entities.MongoDb.Contents
{
    public partial class MongoContentRepository : ISnapshotStore<ContentState, Guid>
    {
        public async Task<(ContentState Value, long Version)> ReadAsync(Guid key)
        {
            var contentEntity =
                await Collection.Find(x => x.Id == key).SortByDescending(x => x.Version)
                    .FirstOrDefaultAsync();

            if (contentEntity != null)
            {
                var schema = await appProvider.GetSchemaAsync(contentEntity.AppId, contentEntity.SchemaId, true);

                if (schema == null)
                {
                    throw new InvalidOperationException($"Cannot find schema {contentEntity.SchemaId}");
                }

                contentEntity?.ParseData(schema.SchemaDef);

                return (SimpleMapper.Map(contentEntity, new ContentState()), contentEntity.Version);
            }

            return (null, EtagVersion.NotFound);
        }

        public async Task WriteAsync(Guid key, ContentState value, long oldVersion, long newVersion)
        {
            var documentId = $"{key}_{newVersion}";

            if (value.SchemaId == Guid.Empty)
            {
                return;
            }

            var schema = await appProvider.GetSchemaAsync(value.AppId, value.SchemaId, true);

            if (schema == null)
            {
                throw new InvalidOperationException($"Cannot find schema {value.SchemaId}");
            }

            var idData = value.Data?.ToIdModel(schema.SchemaDef, true);

            var document = SimpleMapper.Map(value, new MongoContentEntity
            {
                DocumentId = documentId,
                DataText = idData?.ToFullText(),
                DataByIds = idData,
                IsLatest = !value.IsDeleted,
                ReferencedIds = idData?.ToReferencedIds(schema.SchemaDef),
            });

            try
            {
                await Collection.InsertOneAsync(document);
                await Collection.UpdateManyAsync(x => x.Id == value.Id && x.Version < value.Version, Update.Set(x => x.IsLatest, false));
            }
            catch (MongoWriteException ex)
            {
                if (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
                {
                    var existingVersion =
                        await Collection.Find(x => x.Id == value.Id && x.IsLatest).Only(x => x.Id, x => x.Version)
                            .FirstOrDefaultAsync();

                    if (existingVersion != null)
                    {
                        throw new InconsistentStateException(existingVersion["vs"].AsInt64, oldVersion, ex);
                    }
                }
                else
                {
                    throw;
                }
            }
        }
    }
}

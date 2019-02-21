﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschränkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System.Reflection;

namespace Squidex.Infrastructure
{
    public sealed class SquidexInfrastructure
    {
        public static readonly Assembly Assembly = typeof(SquidexInfrastructure).Assembly;
    }
}

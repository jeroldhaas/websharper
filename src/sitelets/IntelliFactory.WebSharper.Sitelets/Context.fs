// $begin{copyright}
//
// This file is part of WebSharper
//
// Copyright (c) 2008-2014 IntelliFactory
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License.  You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.
//
// $end{copyright}

namespace IntelliFactory.WebSharper.Sitelets

/// Provides services available to handlers at run-time.
type Context<'Action> =
    {

        /// Virtual application root path on the server.
        ApplicationPath : string

        /// Generates a (possibly relative) URL to a given action.
        Link : 'Action -> string

        /// The typed JSON provider for interacting with the client.
        Json : IntelliFactory.WebSharper.Core.Json.Provider

        /// WebSharper metadata required for serializing controls.
        Metadata : IntelliFactory.WebSharper.Core.Metadata.Info

        // Generates a URL respecting the application path.
        ResolveUrl : string -> string

        /// WebSharper resource rendering context required for resources.
        ResourceContext : IntelliFactory.WebSharper.Core.Resources.Context

        /// HTTP Request object
        Request : Http.Request

        /// The full path to the root application folder.
        RootFolder : string
    }


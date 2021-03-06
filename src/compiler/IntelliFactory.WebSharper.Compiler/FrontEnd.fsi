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

namespace IntelliFactory.WebSharper.Compiler

/// Exposes the compiler front-end for programmatic use.
module FrontEnd =
    type Assembly = IntelliFactory.WebSharper.Compiler.Assembly
    type Bundle = IntelliFactory.WebSharper.Compiler.Bundle
    type CompiledAssembly = IntelliFactory.WebSharper.Compiler.CompiledAssembly
    type Content = IntelliFactory.WebSharper.Compiler.Content
    type EmbeddedFile = IntelliFactory.WebSharper.Compiler.EmbeddedFile
    type Loader = IntelliFactory.WebSharper.Compiler.Loader
    type ResourceContent = IntelliFactory.WebSharper.Compiler.ResourceContent
    type ResourceContext = IntelliFactory.WebSharper.Compiler.ResourceContext
    type Symbols = IntelliFactory.WebSharper.Compiler.Symbols

    /// Represents compilation options.
    type Options =
        {
            ErrorLimit : int
            KeyPair : option<StrongNameKeyPair>
            References : list<Assembly>
        }

        /// The defaults.
        static member Default : Options

    /// Compiles an assembly and rewrites it on disk. Deprecated. Note
    /// that `Compile opts log a = (Prepare opts log).CompileAndModify(a)`.
    val Compile : Options -> log: (Message -> unit) -> (Assembly -> bool)

    /// Represents the compiler front-end object.
    [<Sealed>]
    type Compiler =

        /// Attempts to compile an expression potentially coming from a dynamic assembly.
        member Compile : quotation: Quotations.Expr * ?name: string -> option<CompiledAssembly>

        /// Attempts to compile an expression potentially coming from a dynamic assembly.
        member Compile : quotation: Quotations.Expr * context: System.Reflection.Assembly * ?name: string -> option<CompiledAssembly>

        /// Compiles an assembly and rewrites it on disk.
        member CompileAndModify : assembly: Assembly -> bool

    /// Prepares a compiler.
    val Prepare : Options -> log: (Message -> unit) -> Compiler


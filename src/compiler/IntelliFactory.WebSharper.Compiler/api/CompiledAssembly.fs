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

module CT = IntelliFactory.WebSharper.Core.ContentTypes
module M = IntelliFactory.WebSharper.Core.Metadata
module P = IntelliFactory.JavaScript.Packager
module PC = IntelliFactory.WebSharper.PathConventions
module R = IntelliFactory.WebSharper.Compiler.ReflectionLayer
module Re = IntelliFactory.WebSharper.Core.Reflection
module Res = IntelliFactory.WebSharper.Core.Resources
module W = IntelliFactory.JavaScript.Writer
type Pref = IntelliFactory.JavaScript.Preferences

[<Sealed>]
type CompiledAssembly
    (
        context: Context,
        source: R.AssemblyDefinition,
        meta: Metadata.T,
        aInfo: M.AssemblyInfo,
        mInfo: M.Info,
        pkg: P.Module,
        typeScript: string
    ) =

    let getJS (pref: Pref) =
        use w = new StringWriter()
        W.WriteProgram pref w (P.Package pkg pref)
        w.ToString()

    let compressedJS = lazy getJS Pref.Compact
    let readableJS = lazy getJS Pref.Readable

    let nameOfSelf = Re.AssemblyName.Convert(source.Name)

    let deps =
        lazy
        let self = M.Node.AssemblyNode(nameOfSelf, M.AssemblyMode.CompiledAssembly)
        mInfo.GetDependencies([self])

    member this.AssemblyInfo = aInfo
    member this.CompressedJavaScript = compressedJS.Value
    member this.Info = mInfo
    member this.Metadata = meta
    member this.Package = pkg
    member this.ReadableJavaScript = readableJS.Value
    member this.TypeScriptDeclarations = typeScript

    member this.Dependencies = deps.Value

    member this.RenderDependencies(ctx: ResourceContext, writer: HtmlTextWriter) =
        let pU = PC.PathUtility.VirtualPaths("/")
        let cache = Dictionary()
        let getRendering (content: ResourceContent) =
            match cache.TryGetValue(content) with
            | true, y -> y
            | _ ->
                let y = ctx.RenderResource(content)
                cache.Add(content, y)
                y
        let makeJsUri (name: PC.AssemblyId) js =
            getRendering {
                Content = js
                ContentType = CT.Text.JavaScript
                Name =
                    if ctx.DebuggingEnabled then
                        pU.JavaScriptPath(name)
                    else
                        pU.MinifiedJavaScriptPath(name)
            }
        let ctx : Res.Context =
            {
                DebuggingEnabled = ctx.DebuggingEnabled
                DefaultToHttp = ctx.DefaultToHttp
                GetAssemblyRendering = fun name ->
                    if name = nameOfSelf then
                        (if ctx.DebuggingEnabled then Pref.Readable else Pref.Compact)
                        |> getJS
                        |> makeJsUri (PC.AssemblyId.Create name.FullName)
                    else
                        match context.LookupAssemblyCode(ctx.DebuggingEnabled, name) with
                        | Some x -> makeJsUri (PC.AssemblyId.Create name.FullName) x
                        | None -> Res.Skip
                GetSetting = ctx.GetSetting
                GetWebResourceRendering = fun ty name ->
                    let (c, cT) = Utility.ReadWebResource ty name
                    getRendering {
                        Content = c
                        ContentType = cT
                        Name = name
                    }
            }
        this.RenderDependencies(ctx, writer)

    member this.RenderDependencies(ctx, writer: HtmlTextWriter) =
        for d in this.Dependencies do
            d.Render ctx writer
        Utility.WriteStartCode true writer

    static member Create
            (
                context: Context,
                source: R.AssemblyDefinition,
                meta: Metadata.T,
                aInfo: M.AssemblyInfo,
                mInfo: M.Info,
                pkg: P.Module,
                typeScript: string
            ) =
        CompiledAssembly(context, source, meta, aInfo, mInfo, pkg, typeScript)

    member this.WriteToCecilAssembly(a: Mono.Cecil.AssemblyDefinition) =
        let pub = Mono.Cecil.ManifestResourceAttributes.Public
        let dep =
            use s = new MemoryStream(8 * 1024)
            Metadata.Serialize s meta
            s.ToArray()
        let prog = P.Package pkg
        let js pref =
            use s = new MemoryStream(8 * 1024)
            let () =
                use w = new StreamWriter(s)
                W.WriteProgram pref w (prog pref)
            s.ToArray()
        let rmdata =
            use s = new MemoryStream(8 * 1024)
            aInfo.ToStream(s)
            s.ToArray()
        let rmname = M.AssemblyInfo.EmbeddedResourceName
        Mono.Cecil.EmbeddedResource(rmname, pub, rmdata)
        |> a.MainModule.Resources.Add
        Mono.Cecil.EmbeddedResource(EMBEDDED_METADATA, pub, dep)
        |> a.MainModule.Resources.Add
        if not pkg.IsEmpty then
            Mono.Cecil.EmbeddedResource(EMBEDDED_MINJS, pub, js Pref.Compact)
            |> a.MainModule.Resources.Add
            Mono.Cecil.EmbeddedResource(EMBEDDED_JS, pub, js Pref.Readable)
            |> a.MainModule.Resources.Add
        Mono.Cecil.EmbeddedResource
            (
                EMBEDDED_DTS, pub,
                UTF8Encoding(false, true).GetBytes(typeScript)
            )
        |> a.MainModule.Resources.Add

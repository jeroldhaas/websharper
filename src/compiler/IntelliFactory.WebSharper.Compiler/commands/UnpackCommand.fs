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

open IntelliFactory.Core
module PC = IntelliFactory.WebSharper.PathConventions
module C = Commands

module UnpackCommand =

    type Config =
        {
            Assemblies : list<string>
            RootDirectory : string
        }

        static member Create() =
            {
                Assemblies = []
                RootDirectory = "."
            }

    let GetErrors config =
        [
            for a in config.Assemblies do
                if C.NoFile a then
                    yield "invalid file: " + a
            if C.NoDir config.RootDirectory then
                yield "-root: invalid directory " + config.RootDirectory
        ]

    let Parse args =
        let rec proc opts xs =
            match xs with
            | [] ->
                opts
            | "-root" :: root :: xs ->
                proc { opts with RootDirectory = root } xs
            | x :: xs ->
                proc { opts with Assemblies = x :: opts.Assemblies } xs
        match args with
        | "-unpack" :: root :: args ->
            let def = Config.Create()
            let cfg = { proc def args with RootDirectory = root }
            match GetErrors cfg with
            | [] -> C.Parsed cfg
            | errors -> C.ParseFailed errors
        | "unpack" :: args ->
            let def = Config.Create()
            let cfg = proc def args
            match GetErrors cfg with
            | [] -> C.Parsed cfg
            | errors -> C.ParseFailed errors
        | _ -> C.NotRecognized

    let Exec env cmd =
        let baseDir =
            let pathToSelf = typeof<Config>.Assembly.Location
            Path.GetDirectoryName(pathToSelf)
        let aR =
            AssemblyResolver.Create()
                .WithBaseDirectory(baseDir)
                .SearchDirectories([baseDir])
        let writeTextFile (output, text) =
            Content.Text(text).WriteFile(output)
        let writeBinaryFile (output, bytes) =
            Binary.FromBytes(bytes).WriteFile(output)
        let pc = PC.PathUtility.FileSystem(cmd.RootDirectory)
        let aR = aR.SearchPaths(cmd.Assemblies)
        let loader = Loader.Create aR stderr.WriteLine
        let emit text path =
            match text with
            | Some text -> writeTextFile (path, text)
            | None -> ()
        let script = PC.ResourceKind.Script
        let content = PC.ResourceKind.Content
        for p in cmd.Assemblies do
            let a = loader.LoadFile p
            let aid = PC.AssemblyId.Create(a.FullName)
            emit a.ReadableJavaScript (pc.JavaScriptPath aid)
            emit a.CompressedJavaScript (pc.MinifiedJavaScriptPath aid)
            emit a.TypeScriptDeclarations (pc.TypeScriptDefinitionsPath aid)
            let writeText k fn c =
                let p = pc.EmbeddedPath(PC.EmbeddedResource.Create(k, aid, fn))
                writeTextFile (p, c)
            let writeBinary k fn c =
                let p = pc.EmbeddedPath(PC.EmbeddedResource.Create(k, aid, fn))
                writeBinaryFile (p, c)
            for r in a.GetScripts() do
                writeText script r.FileName r.Content
            for r in a.GetContents() do
                writeBinary content r.FileName (r.GetContentData())
        C.Ok

    let Description =
        "unpacks resources from WebSharper assemblies"

    let Usage =
        [
            "Usage: WebSharper.exe unpack [OPTIONS] assembly.dll ..."
            "-root <dir>    web project root directory"
        ]
        |> String.concat System.Environment.NewLine

    let Instance =
        C.DefineCommand<Config> "unpack" Description Usage Parse Exec

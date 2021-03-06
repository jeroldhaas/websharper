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

namespace IntelliFactory.WebSharper

module internal TypeScriptGenerator =
    open System
    open System.Collections.Generic
    open System.IO
    open System.Text.RegularExpressions
    open System.Threading
    module MC = MutableCollections
    module Q = QualifiedNames

    [<Struct>]
    type TVar(uid: int64) =
        member tvar.UniqueId = uid

    let mutable tVarCounter = 0L

    let freshTVar () =
        TVar(Interlocked.Increment(&tVarCounter))

    let idPattern =
        Regex(@"^([\w$])+$")

    type Address =
        | Address of Q.Name

        member addr.Name =
            match addr with
            | Address qn -> qn

        override addr.ToString() =
            string addr.Name

        member addr.Builder =
            { QBuilder = addr.Name.Builder }

        member addr.Item
            with get n =
                addr.Builder.Nested(addr, n)

    and AddressBuilder =
        {
            QBuilder : Q.Builder
        }

        member ab.Nested(Address n, x) =
            Address n.[ab.QBuilder.Id(x)]

        member ab.Root(n) =
            Address (ab.QBuilder.Root(ab.QBuilder.Id(n)))

        static member Create() =
            let cfg =
                {
                    Q.Config.Default with
                        IsValidId = idPattern.IsMatch
                }
            { QBuilder = Q.Builder.Create(cfg) }

    type Generic =
        {
            OriginalName : string
            TVar : TVar
        }

    type Contract =
        | CAnonymous of Interface
        | CAny
        | CArray of Contract
        | CBoolean
        | CGeneric of TVar
        | CNamed of Address * list<Contract>
        | CNumber
        | CString
        | CVoid

    and Interface =
        {
            Members : list<Member>
        }

    and Member =
        | MCall of Signature
        | MConstruct of Signature
        | MProperty of string * Contract
        | MMethod of string * Signature
        | MNumber of Contract * string
        | MString of Contract * string

    and Signature =
        {
            ArgumentStack : list<Argument>
            Return : Contract
            SignatureGenerics : Generic []
        }

    and Argument =
        {
            ArgumentContract : Contract
            ArgumentName : string
        }
    type Declaration =
        {
            DeclarationAddress : Address
            DeclarationGenerics : Generic []
        }

    type Definition =
        | TypeDef of Declaration * Interface
        | VarDef of Contract

    type Definitions =
        | Defs of Q.NameMap<Definition>

    type WriteOptions =
        {
            ExportDeclarations : bool
        }

    // Exported API -----------------------------------------------------------

    type Argument with

        static member Create(name, c) =
            {
                ArgumentContract = c
                ArgumentName = name
            }

    [<Sealed>]
    exception InvalidSignatureGeneric of int * int with

        override err.Message =
            match err :> exn with
            | InvalidSignatureGeneric (provided, expected) ->
                String.Format("Invalid generic argument {0} \
                    for a signature of generic arity {1}", provided, expected)
            | _ -> "impossible"

    type Signature with

        member s.Item
            with get (pos) =
                let n = s.SignatureGenerics.Length
                if pos >= 0 && pos < n then
                    CGeneric s.SignatureGenerics.[pos].TVar
                else
                    raise (InvalidSignatureGeneric (pos, n))

        member s.WithArgument(name, ct) =
            s.WithArgument(Argument.Create(name, ct))

        member s.WithArgument(arg) =
            { s with ArgumentStack = arg :: s.ArgumentStack }

        member s.WithReturn(ct) =
            { s with Return = ct }

        static member Create(?generics) =
            let gs = defaultArg generics []
            {
                ArgumentStack = []
                Return = CVoid
                SignatureGenerics =
                    List.toArray gs
                    |> Array.map (fun g ->
                        {
                            OriginalName = g
                            TVar = freshTVar ()
                        })
            }

    type Member with

        static member ByNumber(contract, ?name) =
            MNumber (contract, defaultArg name "item")

        static member ByString(contract, ?name) =
            MString (contract, defaultArg name "item")

        static member Call(s) =
            MCall s

        static member Construct(s) =
            MConstruct(s)

        static member Method(name, sign) =
            MMethod (name, sign)

        static member NumericMethod(pos: int, sign) =
            MMethod (string pos, sign)

        static member NumericProperty(pos: int, contract) =
            MProperty (string pos, contract)

        static member Property(name, contract) =
            MProperty(name, contract)

    type Interface with

        static member Create(ms) =
            { Members = Seq.toList ms }

    [<Sealed>]
    exception InvalidTypeGeneric of Address * int * int with

        override err.Message =
            match err :> exn with
            | InvalidTypeGeneric (addr, provided, expected) ->
                String.Format("Invalid generic argument {1} \
                    for named contract {0}`{2}", addr, provided, expected)
            | _ -> "impossible"

    type Declaration with

        member decl.At(pos) =
            let n = decl.DeclarationGenerics.Length
            if pos >= 0 && pos < n then
                CGeneric decl.DeclarationGenerics.[pos].TVar
            else
                let a = decl.DeclarationAddress
                raise (InvalidTypeGeneric (a, pos, n))

        member decl.Item with get (pos) = decl.At(pos)

        static member Create(addr, ?gs) =
            let gs = defaultArg gs []
            {
                DeclarationAddress = addr
                DeclarationGenerics =
                    List.toArray gs
                    |> Array.map (fun g ->
                        {
                            OriginalName = g
                            TVar = freshTVar ()
                        })
            }

    type Definitions with

        static member Define(decl, i) =
            Q.NameMap.Singleton(decl.DeclarationAddress.Name, TypeDef (decl, i))
            |> Defs

        static member Merge(defs) =
            seq { for Defs d in defs -> d }
            |> Q.NameMap.Merge
            |> Defs

        static member Var(Address addr, con) =
            Q.NameMap.Singleton(addr, VarDef con)
            |> Defs

    exception InvalidGenericArgumentCount of Address with

        override err.Message =
            match err :> exn with
            | InvalidGenericArgumentCount addr -> string addr
            | _ -> "impossible"

    type Contract with

        static member Anonymous(t) =
            CAnonymous t

        static member Array(t) =
            CArray t

        static member Generic(g: Declaration, pos) =
            g.[pos]

        static member Generic(s: Signature, pos) =
            s.[pos]

        static member Named(decl, ?gs) =
            let gs = defaultArg gs []
            if decl.DeclarationGenerics.Length <> gs.Length then
                raise (InvalidGenericArgumentCount decl.DeclarationAddress)
            CNamed (decl.DeclarationAddress, gs)

        static member Any = CAny
        static member Boolean = CBoolean
        static member Number = CNumber
        static member String = CString
        static member Void = CVoid

    // ------------------------------------------------------------------------

    type IndentedWriter =
        {
            mutable Column : int
            mutable Indent : int
            IndentString : string
            Writer : TextWriter
        }

        member inline pc.Delay(f) =
            let old = pc.Indent
            pc.Indent <- pc.Indent + 1
            let r = f ()
            pc.Indent <- old
            r

        member inline pc.Zero() =
            ()

        static member Create(w) =
            {
                Column = 0
                Indent = 0
                IndentString = "    "
                Writer = w
            }

    type Scope =
        {
            Generics : Map<TVar,Generic>
        }

    let globalScope =
        { Generics = Map.empty }

    type AliasTable =
        {
            ModuleAliases : Dictionary<Q.Name,Q.Id>
            TypeAliases : Dictionary<Q.Name,Q.Id>
            UsedNames : HashSet<Q.Id>
        }

        member at.UseName(id) =
            at.UsedNames.Add(id) |> ignore

        static member Create(usedNames: seq<Q.Id>) =
            {
                ModuleAliases = Dictionary()
                TypeAliases = Dictionary()
                UsedNames = HashSet(usedNames)
            }

    type PrintContext =
        {
            AliasTable : AliasTable
            Builder : Q.Builder
            IndentedWriter : IndentedWriter
            Scope : Scope
        }

        static member Create(aT, w, builder) =
            {
                AliasTable = aT
                Builder = builder
                IndentedWriter = IndentedWriter.Create(w)
                Scope = globalScope
            }

    let write pc (text: string) =
        let iw = pc.IndentedWriter
        let w = iw.Writer
        let iS = iw.IndentString
        if iw.Column = 0 then
            for i in 1 .. iw.Indent do
                w.Write(iS)
        w.Write(text)
        iw.Column <- iw.Column + iw.Indent * iS.Length + text.Length

    let writeLine pc s =
        let iw = pc.IndentedWriter
        write pc s
        iw.Writer.WriteLine()
        iw.Column <- 0

    let inline indent pc =
        pc.IndentedWriter

    let inline cached (table: Dictionary<_,_>) x f =
        match table.TryGetValue(x) with
        | true, r -> r
        | _ ->
            let r = f x
            table.[x] <- r
            r

    let inline pickName ok start =
        let rec loop n =
            let name =
                match n with
                | 0 -> start
                | k -> start + string k
            if ok name then name else loop (n + 1)
        loop 0

    let rec qNameRoot n =
        match n with
        | Q.Root x -> x
        | Q.Nested (x, _) -> qNameRoot x

    let aliasInTable (tab: AliasTable) (Address qn as addr) =
        let qb = qn.Builder
        let abbrev = qb.Root(qb.Id("__ABBREV"))
        match qn with
        | Q.Nested (ns, name) ->
            let shortModuleName =
                cached tab.ModuleAliases ns <| fun ns ->
                    let moduleName =
                        "__" + ns.Id.Text
                        |> pickName (fun n ->
                            let id = qb.Id(n)
                            tab.UsedNames.Add(id))
                    qb.Id(moduleName)
            abbrev.[shortModuleName].[name]
            |> Address
        | Q.Root name ->
            let shortName =
                cached tab.TypeAliases qn <| fun qn ->
                    let typeName =
                        "__" + name.Text
                        |> pickName (fun n ->
                            let id = qb.Id(n)
                            tab.UsedNames.Add(id))
                    qb.Id(typeName)
            abbrev.[shortName]
            |> Address

    let alias pc addr =
        aliasInTable pc.AliasTable addr

    let writeAliasTable pc =
        writeLine pc "declare module __ABBREV {"
        indent pc {
            let tab = pc.AliasTable
            do  writeLine pc ""
                for KeyValue (k, v) in tab.ModuleAliases do
                    write pc "export import "
                    write pc v.Text
                    write pc " = "
                    write pc k.Text
                    writeLine pc ";"
                for KeyValue (k, v) in tab.TypeAliases do
                    write pc "interface "
                    write pc v.Text
                    write pc " extends "
                    write pc k.Text
                    writeLine pc " {}"
        }
        writeLine pc "}"

    let writeAddress pc addr =
        let addr = alias pc addr
        write pc addr.Name.Text

    exception InvalidGeneric

    /// TODO: better name clash detection/disambiguation for generic parameters.

    let writeGeneric pc g =
        write pc "_"
        write pc g.OriginalName

    let writeTVar pc tv =
        match pc.Scope.Generics.TryFind(tv) with
        | None -> raise InvalidGeneric
        | Some g -> writeGeneric pc g

    let writeCommaSeparated wr pc xs =
        match xs with
        | [] -> ()
        | head :: xs ->
            wr pc head
            for x in xs do
                write pc ", "
                wr pc x

    let writeGenerics pc gs =
        match gs with
        | [||] -> ()
        | gs ->
            write pc "<"
            writeCommaSeparated writeGeneric pc (List.ofArray gs)
            write pc ">"

    let emptyVector =
        Q.IdVector.Create([])

    let inGenericContext pc gs =
        let gs =
            Array.fold
                (fun gs g -> Map.add g.TVar g gs)
                pc.Scope.Generics
                gs
        { pc with Scope = { Generics = gs } }

    let rec writeContract pc c =
        match c with
        | CAnonymous i -> writeInterface pc i
        | CAny -> write pc "any"
        | CArray c ->
            writeContract pc c
            write pc "[]"
        | CBoolean -> write pc "boolean"
        | CGeneric v -> writeTVar pc v
        | CNamed (name, []) -> writeAddress pc name
        | CNamed (name, inst) ->
            writeAddress pc name
            write pc "<"
            writeCommaSeparated writeContract pc inst
            write pc ">"
        | CNumber -> write pc "number"
        | CString -> write pc "string"
        | CVoid -> write pc "void"

    and writeInterface pc i =
        writeLine pc "{"
        indent pc {
            do for m in i.Members do
                writeMember pc m
        }
        write pc "}"

    and writeMember pc m =
        match m with
        | MCall sign ->
            writeSignature pc sign
        | MConstruct sign ->
            write pc "new"
            writeSignature pc sign
        | MMethod (name, sign) ->
            write pc name
            writeSignature pc sign
        | MNumber (contract, name) ->
            write pc "["
            writeArgument pc (Argument.Create(name, CNumber))
            write pc "]: "
            writeContract pc contract
        | MProperty (name, c) ->
            write pc name
            write pc ": "
            writeContract pc c
        | MString (contract, name) ->
            write pc "["
            writeArgument pc (Argument.Create(name, CString))
            write pc "]: "
            writeContract pc contract
        writeLine pc ";"

    and writeSignature pc s =
        let pc = inGenericContext pc s.SignatureGenerics
        writeGenerics pc s.SignatureGenerics
        write pc "("
        writeCommaSeparated writeArgument pc (List.rev s.ArgumentStack)
        write pc ")"
        write pc ": "
        writeContract pc s.Return

    and writeArgument pc arg =
        let n = IntelliFactory.JavaScript.Identifier.MakeValid arg.ArgumentName
        write pc n
        write pc ": "
        writeContract pc arg.ArgumentContract

    exception UndefinedDeclaration of Q.Name with

        override err.Message =
            match err :> exn with
            | UndefinedDeclaration a -> a.Text
            | _ -> "impossible"

    let verifyPromises (Defs defs) =
        let verify addr =
            match defs.TryFind(addr) with
            | Some (TypeDef _) -> ()
            | _ -> raise (UndefinedDeclaration addr)
        let rec visitContract c =
            match c with
            | CAnonymous i -> visitInterface i
            | CArray c -> visitContract c
            | CNamed (addr, gs) ->
                verify addr.Name
                List.iter visitContract gs
            | CGeneric _ | CAny | CBoolean | CNumber | CString | CVoid -> ()
        and visitSignature (s: Signature) =
            for a in s.ArgumentStack do
                visitContract a.ArgumentContract
            visitContract s.Return
        and visitInterface i =
            for m in i.Members do
                match m with
                | MMethod (_, s) | MCall s | MConstruct s -> visitSignature s
                | MProperty (_, c) | MNumber (c, _) | MString (c, _) -> visitContract c
        let visit addr v =
            match v with
            | TypeDef (_, i) -> visitInterface i
            | VarDef c -> visitContract c
        defs.Iterate(visit)

    type Location =
        | AtGlobalModule
        | AtModule of Q.Name

    type Data =
        {
            DefinitionMap : MC.MultiDictionary<Location, Q.Name * Definition>
            ModuleMap : MC.DictionarySet<Location, Q.Name>
            mutable NameBuilder : Q.Builder
        }

        static member private Create() =
            {
                DefinitionMap = MC.MultiDictionary()
                ModuleMap = MC.DictionarySet()
                NameBuilder = Q.Builder.Create()
            }

        static member Build(Defs defs) =
            let ({ DefinitionMap = dm; ModuleMap = mm } as data) = Data.Create()
            let visitedModules = HashSet<Q.Name>()
            let rec visitModule name =
                if visitedModules.Add(name) then
                    match name with
                    | Q.Root n ->
                        mm.Add(AtGlobalModule, name)
                    | Q.Nested(parent, _) ->
                        visitModule parent
                        mm.Add(AtModule parent, name)
            let visitDefinition (name: Q.Name) def =
                data.NameBuilder <- name.Builder
                match name with
                | Q.Root n ->
                    dm.Add(AtGlobalModule, (name, def))
                | Q.Nested (p, n) ->
                    visitModule p
                    dm.Add(AtModule p, (name, def))
            defs.Iterate(visitDefinition)
            data

    let queryDefinitions data loc =
        data.DefinitionMap.Find(loc)

    let queryModules data loc =
        data.ModuleMap.Find(loc)

    let local (name: Q.Name) =
        name.Id.Text

    let writeDefinition pc def addr =
        match def with
        | VarDef c ->
            write pc "var "
            write pc (local addr)
            write pc " : "
            writeContract pc c
            writeLine pc ";"
        | TypeDef (d, c) ->
            write pc "interface "
            let pc = inGenericContext pc d.DeclarationGenerics
            write pc (local addr)
            writeGenerics pc d.DeclarationGenerics
            write pc " "
            writeInterface pc c
            writeLine pc ""

    let rec writeModule pc data addr =
        write pc "module "
        write pc (local addr)
        writeLine pc " {"
        indent pc { writeModuleBody pc data (AtModule addr) }
        writeLine pc "}"

    and writeModuleBody pc data sc =
        for ss in queryModules data sc do
            writeModule pc data ss
        for (n, d) in queryDefinitions data sc do
            writeDefinition pc d n

    let writeDefs opts pc data defs =
        let exp = opts.ExportDeclarations
        for (n, d) in queryDefinitions data AtGlobalModule do
            if exp then
                write pc "export "
            write pc "declare "
            writeDefinition pc d n
        for m in queryModules data AtGlobalModule do
            if exp then
                write pc "export "
            write pc "declare "
            writeModule pc data m

    let getGloballyUsedNames (data: Data) =
        seq {
            let sc = Location.AtGlobalModule
            for ss in queryModules data sc do
                yield ss.Id
            for (n, d) in queryDefinitions data sc do
                yield n.Id
        }

    let writeDefinitions opts defs w =
        let data = Data.Build(defs)
        let usedNames = getGloballyUsedNames data
        let aT = AliasTable.Create(usedNames)
        let pc = PrintContext.Create(aT, w, data.NameBuilder)
        writeDefs opts pc data defs
        writeAliasTable pc

    type Definitions with

        member defs.Write(w, ?opts) =
            let opts = defaultArg opts { ExportDeclarations = false }
            writeDefinitions opts defs w

        member defs.Verify() =
            verifyPromises defs


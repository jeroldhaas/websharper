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

/// Implements a monadic command-line option parser framework.
module internal IntelliFactory.WebSharper.Arguments

open System
open System.IO

/// Results of parsing.
type Parse<'T> =

    /// Recoverable error: try backtracking.
    | Failed of string

    /// Irrecoverable error: do not backtrack.
    | Invalid of string

    /// Parse success.
    | Parsed of 'T * list<string>

type Parser<'T> =
    {
        keywords : list<string * string>
        parse : list<string> -> Parse<'T>
        sample : 'T
    }

type ParserBuilder =
    | Do

    member this.Return x =
        {
            keywords = []
            parse = fun xs -> Parsed (x, xs)
            sample = x
        }

    member this.Bind(x, f) =
        {
            keywords = x.keywords @ (f x.sample).keywords
            sample = (f x.sample).sample
            parse = fun xs ->
                match x.parse xs with
                | Parsed (x, xs) -> (f x).parse xs
                | Invalid r -> Invalid r
                | Failed r -> Failed r
        }

let Map (f: 'T1 -> 'T2) (x: Parser<'T1>) : Parser<'T2> =
    {
        parse = fun xs ->
            match x.parse xs with
            | Parsed (r, xs) -> Parsed (f r, xs)
            | Failed r -> Failed r
            | Invalid r -> Invalid r
        keywords = x.keywords
        sample = f x.sample
    }

let Filter test x =
    let parse xs =
        match x.parse xs with
        | Parsed (r, xs) ->
            match test r with
            | None -> Parsed (r, xs)
            | Some msg -> Failed msg
        | Failed r ->
            Failed r
        | Invalid r ->
            Invalid r
    { x with parse = parse }

let Many (x: Parser<'T>) : Parser<list<'T>> =
    let rec p xs =
        match x.parse xs with
        | Parsed (i, xs) ->
            match p xs with
            | Parsed (is, xs) -> Parsed (i :: is, xs)
            | Failed _ -> Parsed ([i], xs)
            | Invalid r -> Invalid r
        | Failed _ -> Parsed ([], xs)
        | Invalid r -> Invalid r
    {
        keywords = x.keywords
        sample = []
        parse = p
    }

let Several (x: Parser<'T>) : Parser<list<'T>> =
    Do {
        let! r = x
        let! rs = Many x
        return r :: rs
    }

let Optional (x: Parser<'T>) : Parser<option<'T>> =
    {
        keywords = x.keywords
        sample = None
        parse = fun xs ->
            match x.parse xs with
            | Parsed (r, xs) -> Parsed (Some r, xs)
            | Failed _ -> Parsed (None, xs)
            | Invalid r -> Invalid r
    }

let Default (d: 'T) (p: Parser<'T>) : Parser<'T> =
    Map (fun x -> defaultArg x d) (Optional p)

let Tuple2 a b =
    Do {
        let! x = a
        let! y = b
        return (x, y)
    }

let Tuple3 a b c =
    Do {
        let! x = a
        let! y = b
        let! z = c
        return (x, y, z)
    }

let Expect keyword =
    let p xs =
        match xs with
        | x :: xs when x = keyword ->
            Parsed ((), xs)
        | x :: xs ->
            let fmt = "Unexpected input: {0}. Expecting: {1}"
            Failed (String.Format(fmt, x, keyword))
        | [] ->
            Failed ("Unexpected end of input. Expecting: " + keyword)
    {
        keywords = []
        sample = ()
        parse = p
    }

let Free p =
    let rec search acc p xs =
        match p.parse xs with
        | Parsed (r, xs) ->
            Parsed (r, List.rev acc @ xs)
        | Failed r ->
            match xs with
            | x :: xs -> search (x :: acc) p xs
            | [] -> Failed r
        | Invalid r ->
            Invalid r
    { p with parse = search [] p }

let Invalidate p =
    {
        p with
            parse = fun x ->
                match p.parse x with
                | Failed x -> Invalid x
                | r -> r
    }

let Keyword kw doc p =
    let parser =
        Do {
            do! Expect kw
            let! r = Invalidate p
            return r
        }
        |> Free
    {
        keywords = if String.IsNullOrEmpty doc then [] else [(kw, doc)]
        sample = p.sample
        parse = parser.parse
    }

let String =
    let p xs =
        match xs with
        | [] -> Failed "Unexpected end of input."
        | x :: xs -> Parsed (x, xs)
    {
        keywords = []
        parse = p
        sample = ""
    }

let Scan<'T when 'T :> System.ValueType> s : Parser<'T> =
    let kind = typeof<'T>.Name
    let parse xs =
        match xs with
        | x :: xs ->
            match s x with
            | true, r -> Parsed (r, xs)
            | _ ->
                let fmt = "Not a valid {0}: {1}."
                Failed (System.String.Format(fmt, kind, x))
        | [] ->
            Failed ("Unexpected end of input. Expecting: " + kind)
    {
        keywords = []
        parse = parse
        sample = Unchecked.defaultof<_>
    }

let Boolean         = Scan System.Boolean.TryParse
let Byte            = Scan System.Byte.TryParse
let Char            = Scan System.Char.TryParse
let Double          = Scan System.Double.TryParse
let DateTime        = Scan System.DateTime.TryParse
let DateTimeOffset  = Scan System.DateTimeOffset.TryParse
let Decimal         = Scan System.Decimal.TryParse
let Int16           = Scan System.Int16.TryParse
let Int32           = Scan System.Int32.TryParse
let Int64           = Scan System.Int64.TryParse
let SByte           = Scan System.SByte.TryParse
let Single          = Scan System.Single.TryParse
let TimeSpan        = Scan System.TimeSpan.TryParse
let UInt16          = Scan System.UInt16.TryParse
let UInt32          = Scan System.UInt16.TryParse
let UInt64          = Scan System.UInt16.TryParse

let Help (usage: string) p =
    use w = new StringWriter()
    w.WriteLine usage
    w.WriteLine()
    let longest =
        match p.keywords with
        | [] -> 0
        | _  -> List.max (p.keywords |> List.map (fun (kw, _) -> kw.Length))
    for (kw, doc) in p.keywords do
        w.WriteLine("  {0}  {1}", kw.PadRight longest, doc)
    w.ToString()

let Parse p inputs =
    match p.parse inputs with
    | Parsed (r, []) -> Choice1Of2 r
    | Parsed (_, x::xs) -> Choice2Of2 ("Unexpected input: " + x)
    | Failed r| Invalid r -> Choice2Of2 r

let Run inputs usage p main =
    let scanner x =
        match x with
        | "-help" | "--help" | "/?" | "/h" | "/help" -> (true, true)
        | _ -> (false, true)
    match (Free (Scan scanner)).parse inputs with
    | Parsed (_, _) ->
        stderr.WriteLine(Help usage p)
        -1
    | _ ->
        match Parse p inputs with
        | Choice1Of2 x -> main x
        | Choice2Of2 x ->
            stderr.WriteLine x
            stderr.WriteLine()
            stderr.WriteLine(Help usage p)
            -1

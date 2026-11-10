module Ballast.Analysis.Classifier

open FSharp.Compiler.Symbols
open Ballast.Core

/// IsFunctionType does not look through abbreviations such as a Clock field.
let rec private isFunction (fsharpType: FSharpType) =
    fsharpType.IsFunctionType
    || (fsharpType.IsAbbreviation && isFunction fsharpType.AbbreviatedType)

let private isFunctionRecord (entity: FSharpEntity) =
    entity.IsFSharpRecord
    && entity.FSharpFields.Count > 0
    && entity.FSharpFields |> Seq.forall (fun field -> isFunction field.FieldType)

/// The classification under the fsharp profile and the classic profile.
let private abstractionsOf (entity: FSharpEntity) =
    if entity.IsMeasure then
        NotCounted, NotCounted
    elif entity.IsFSharpAbbreviation then
        if isFunction entity.AbbreviatedType then
            Abstract, NotCounted
        else
            NotCounted, NotCounted
    elif entity.IsInterface || entity.IsAbstractClass then
        Abstract, Abstract
    elif entity.IsDelegate || isFunctionRecord entity then
        Abstract, Concrete
    elif entity.IsFSharpModule && entity.MembersFunctionsAndValues.Count = 0 then
        NotCounted, NotCounted
    else
        Concrete, Concrete

/// The abstraction under the fsharp profile, under the classic profile, and whether it is a data type.
let classify (entity: FSharpEntity) : Abstraction * Abstraction * bool =
    let fsharp, classic = abstractionsOf entity
    fsharp, classic, entity.IsFSharpRecord || entity.IsFSharpUnion || entity.IsEnum

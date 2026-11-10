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

let rec private withoutAbbreviations (fsharpType: FSharpType) =
    if fsharpType.IsAbbreviation then
        withoutAbbreviations fsharpType.AbbreviatedType
    else
        fsharpType

/// A function's final return type, after all its arguments.
let rec private finalReturn (fsharpType: FSharpType) =
    let fsharpType = withoutAbbreviations fsharpType

    if fsharpType.IsFunctionType then
        finalReturn fsharpType.GenericArguments[1]
    else
        fsharpType

let private isParameter (name: string) (fsharpType: FSharpType) =
    let fsharpType = withoutAbbreviations fsharpType
    fsharpType.IsGenericParameter && fsharpType.GenericParameter.Name = name

/// A free-monad instruction set, such as `ReadUser of UserId * (User -> 'next)`: every case ends in a type
/// parameter, or in a function returning it, and at least one case has such a function. It describes
/// operations and leaves an interpreter to decide what they do, so it is an effect interface.
let private isInstructionUnion (entity: FSharpEntity) =
    let cases = entity.UnionCases |> Seq.toList

    let lastFieldType (case: FSharpUnionCase) =
        if case.Fields.Count = 0 then None else Some case.Fields[case.Fields.Count - 1].FieldType

    entity.IsFSharpUnion
    && not cases.IsEmpty
    && entity.GenericParameters
       |> Seq.exists (fun parameter ->
           let returnsParameter (fieldType: FSharpType) =
               isFunction fieldType && isParameter parameter.Name (finalReturn fieldType)

           cases
           |> List.forall (fun case ->
               lastFieldType case
               |> Option.exists (fun fieldType -> isParameter parameter.Name fieldType || returnsParameter fieldType))
           && cases |> List.exists (fun case -> lastFieldType case |> Option.exists returnsParameter))

/// The classification under the functional profile and the classic profile.
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
    elif entity.IsDelegate || isFunctionRecord entity || isInstructionUnion entity then
        Abstract, Concrete
    elif entity.IsFSharpModule && entity.MembersFunctionsAndValues.Count = 0 then
        NotCounted, NotCounted
    else
        Concrete, Concrete

/// The abstraction under the functional profile, under the classic profile, and whether it is a data type.
let classify (entity: FSharpEntity) : Abstraction * Abstraction * bool =
    let functional, classic = abstractionsOf entity
    functional, classic, entity.IsFSharpRecord || entity.IsFSharpUnion || entity.IsEnum

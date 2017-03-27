﻿namespace FSharpMP3

open Utils
open Header
open Frame
open Sideinfo
open HuffmanTables

module ScaleFactors = 
    type ScaleFactorsLong = {
        granule:int
        channel:int
        data:int []
    }

    type ScaleFactorsShort = {
        granule:int
        channel:int
        data:int [,]
    }

    type ScaleFactors = 
    |Short of ScaleFactorsShort
    |Long of ScaleFactorsLong
    |Mixed of (ScaleFactorsShort * ScaleFactorsLong)

    let parseScaleFactors (x:array<byte>) (y:sideInfoGranule) = 
        let mutable bitcount = 0
        let slen = 
            [|
                [|0;0|];[|0;1|];[|0;2|];[|0;3|];[|3;0|];[|1;1|];[|1;2|];[|1;3|];
                [|2;1|];[|2;2|];[|2;3|];[|3;1|];[|3;2|];[|3;3|];[|4;2|];[|4;3|];
            |]
        let scaleFactorLengths = [|slen.[y.scaleFactorCompress].[0];slen.[y.scaleFactorCompress].[1]|]
        let (result:ScaleFactors) = 
            match (y.blockType,y.windowSwitchFlag) with
            |(2,true) -> //Mixed/Short scalefactors
                match y.mixedBlockFlag with
                |true -> //Mixed
                    let (longScale:ScaleFactorsLong) = {
                        granule = y.granule
                        channel = y.channel
                        data = 
                            let temp = Array.zeroCreate 8
                            let mutable arr = x
                            for i = 0 to 7 do
                                let (num,tmp) = arr |> getBits scaleFactorLengths.[0]
                                arr <- tmp
                                temp.[i] <- num
                            bitcount <- bitcount + (8 * scaleFactorLengths.[0])
                            temp
                    }
                    let (shortScale:ScaleFactorsShort) = {
                        granule = y.granule
                        channel = y.channel
                        data = 
                            let temp = Array2D.zeroCreate 3 13
                            let mutable arr = x
                            for sfb = 3 to 5 do
                                for win = 0 to 2 do
                                    let (num,tmp) = arr |> getBits scaleFactorLengths.[0]
                                    arr <- tmp
                                    temp.[win,sfb] <- num
                            for sfb = 6 to 11 do
                                for win = 0 to 2 do
                                    let (num,tmp) = arr |> getBits scaleFactorLengths.[1]
                                    arr <- tmp
                                    temp.[win,sfb] <- num
                            bitcount <- bitcount + (9 * scaleFactorLengths.[0])
                            bitcount <- bitcount + (18 * scaleFactorLengths.[1])
                            temp
                    }
                    Mixed (shortScale,longScale)
                |false -> //Short
                    let (shortScale:ScaleFactorsShort) = {
                        granule = y.granule
                        channel = y.channel
                        data = 
                            let temp = Array2D.zeroCreate 3 13
                            let mutable arr = x
                            for sfb = 0 to 5 do
                                for win = 0 to 2 do
                                    let (num,tmp) = arr |> getBits scaleFactorLengths.[0]
                                    arr <- tmp
                                    temp.[win,sfb] <- num
                            for sfb = 6 to 11 do
                                for win = 0 to 2 do
                                    let (num,tmp) = arr |> getBits scaleFactorLengths.[1]
                                    arr <- tmp
                                    temp.[win,sfb] <- num
                            temp
                    }
                    bitcount <- bitcount + (18 * scaleFactorLengths.[0])
                    bitcount <- bitcount + (18 * scaleFactorLengths.[1])
                    Short (shortScale)
            |(_,_) -> //Long scalefactors
                match y.granule = 0 with
                |true -> //Granule 0
                    let (longScale:ScaleFactorsLong) = {
                        granule = y.granule
                        channel = y.channel
                        data = 
                            let temp = Array.zeroCreate 20
                            let mutable arr = x
                            for sfb = 0 to 9 do
                                let (num,tmp) = arr |> getBits scaleFactorLengths.[0]
                                arr <- tmp
                                temp.[sfb] <- num
                            for sfb = 10 to 19 do
                                let (num,tmp) = arr |> getBits scaleFactorLengths.[0]
                                arr <- tmp
                                temp.[sfb] <- num
                            temp
                    }
                    bitcount <- bitcount + 20 * scaleFactorLengths.[0]
                    Long (longScale)
                |false -> //Granule 1 (maybe with scalefactor reuse)
                    let (longScale:ScaleFactorsLong) = {
                        granule = y.granule
                        channel = y.channel
                        data = Array.zeroCreate 20 //Handle this later
                    }
                    bitcount <- bitcount + 20 * scaleFactorLengths.[0]
                    Long (longScale)
        (result,bitcount)

module Huffman = 
    let parseHuffmanData (data:array<byte>) (frame:FrameInfo) (granule:sideInfoGranule) = 
        let samples = Array.zeroCreate 576
        let mutable bitsArray = data
        let mutable bitcount = 0
        let mutable samplecount = 0

        let (region0,region1) = //Get boundaries of sample regions
            match(granule.blockType,granule.windowSwitchFlag) with
            |(2,true) -> (36,576)
            |_ -> 
                let reg0 = (frame.bandIndex |> fst).[granule.region0Count + 1]
                let reg1 = (frame.bandIndex |> fst).[granule.region0Count + granule.region1Count + 2]
                (reg0,reg1)
        
        let getTable x = //x = sample number
            match x < region0 with
            |true -> ((getHuffmanTable granule.tableSelect.[0]),(granule.tableSelect.[0]))
            |false ->
                match x < region1 with
                |true -> ((getHuffmanTable granule.tableSelect.[1]),(granule.tableSelect.[1]))
                |false -> ((getHuffmanTable granule.tableSelect.[2]),(granule.tableSelect.[2]))

        let decodeTable x = //x = huffman table
            match (snd x) with
            |0 -> (0,0,0,0) //Sample = 0 for table0
            |_ -> 
                let mutable row = 0
                let rec checkInTable table (pattern:uint32) = 
                    match table with
                    |[] -> (0,0,0,snd x)
                    |head::tail -> 
                        if List.exists (fun (value,size) -> (value = ((pattern >>> (32 - size)) |> int))) head
                            then 
                                (
                                    //Size
                                    head.[List.findIndex (fun (value,size) -> (value = ((pattern >>> (32 - size)) |> int))) head] |> snd,
                                    //Row
                                    row,
                                    //Col
                                    List.findIndex (fun (value,size) -> (value = ((pattern >>> (32 - size)) |> int))) head,
                                    //Table number
                                    snd x
                                )
                            else
                                row <- row + 1
                                checkInTable tail pattern

                let (bits,_) = getBits32 32 bitsArray
                let (size,row,col,num) = bits |> checkInTable (fst x)
                bitcount <- bitcount + size
                if size <> 0 then bitsArray <- bitsArray.[size..]
                (size,row,col,num)
        
        let getSample x = //x = output from decodeTable
            let (size,row,col,num) = x
            match (size,row,col) with
            |(0,0,0) -> [0;0]
            |(_,_,_) ->
                let extendSample y value = //y = table num
                    let linbit = 
                        match bigValueLinbit.[num] <> 0, value = (bigValueMax.[num] - 1) with
                        |(true,true) -> 
                            let (bits,temp) = getBits bigValueLinbit.[num] bitsArray
                            bitcount <- bitcount + bigValueLinbit.[num]
                            bitsArray <- temp
                            bits
                        |(_,_) -> 0
                    let sign = 
                        if value > 0 
                            then 
                                let (bits,temp) = getBits 1 bitsArray
                                bitcount <- bitcount + 1
                                bitsArray <- temp
                                if bits = 1 then -1 else 1
                            else
                                1
                    sign * (value + linbit)
                [row;col] |> List.map (extendSample num)
        
        //Decode huffman tables
        let limit = granule.bigValues * 2
        while samplecount < (limit) do
            let result = samplecount |> (getTable >> decodeTable >> getSample)
            samples.[samplecount] <- result.[0]
            samples.[samplecount + 1] <- result.[1]
            samplecount <- samplecount + 2
        samples

module Maindata = 
    
    open ScaleFactors
    open Huffman
    
    //Parse Main data from frame
    let parseMainData (data:array<byte>) (header:HeaderConfig) (sideinfo:SideInfoConfig) = 
        let arrayBits = data |> Array.map int |> getBitsArrayfromByteArray
        let mutable bitcount = 0
        let frameinfo = getFrameInfo header
        let (sclfactors:array<ScaleFactors>) = Array.zeroCreate 4
        let bitcounts = Array.zeroCreate 4
        for i = 0 to 3 do
            let maxbit = bitcount + sideinfo.sideInfoGr.[i].par23Length
            let (x,y) = parseScaleFactors (arrayBits.[bitcount..] |> Array.map byte) sideinfo.sideInfoGr.[i]
            sclfactors.[i] <- x
            bitcount <- bitcount + y
            let result = parseHuffmanData (arrayBits.[bitcount..maxbit+y] |> Array.map byte) frameinfo sideinfo.sideInfoGr.[i]
            bitcount <- maxbit
        sclfactors
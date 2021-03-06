﻿namespace MsharP3

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

    let parseScaleFactors (data:array<byte>) (sideInfoConfig:SideInfoConfig) (y:sideInfoGranule) = 
        //For keeping track of offset
        let mutable bitoffset = 0

        let slen = [|
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
                            for i = 0 to 7 do
                                let (num,tmp) = data |> getBits2 bitoffset scaleFactorLengths.[0]
                                bitoffset <- tmp
                                temp.[i] <- num
                            temp
                    }
                    let (shortScale:ScaleFactorsShort) = {
                        granule = y.granule
                        channel = y.channel
                        data = 
                            let temp = Array2D.zeroCreate 3 13
                            for sfb = 3 to 5 do
                                for win = 0 to 2 do
                                    let (num,tmp) = data |> getBits2 bitoffset scaleFactorLengths.[0]
                                    bitoffset <- tmp
                                    temp.[win,sfb] <- num
                            for sfb = 6 to 11 do
                                for win = 0 to 2 do
                                    let (num,tmp) = data |> getBits2 bitoffset scaleFactorLengths.[1]
                                    bitoffset <- tmp
                                    temp.[win,sfb] <- num
                            temp
                    }
                    Mixed (shortScale,longScale)
                |false -> //Short
                    let (shortScale:ScaleFactorsShort) = {
                        granule = y.granule
                        channel = y.channel
                        data = 
                            let temp = Array2D.zeroCreate 3 13
                            let mutable arr = data
                            for sfb = 0 to 5 do
                                for win = 0 to 2 do
                                    let (num,tmp) = data |> getBits2 bitoffset scaleFactorLengths.[0]
                                    bitoffset <- tmp
                                    temp.[win,sfb] <- num
                            for sfb = 6 to 11 do
                                for win = 0 to 2 do
                                    let (num,tmp) = data |> getBits2 bitoffset scaleFactorLengths.[1]
                                    bitoffset <- tmp
                                    temp.[win,sfb] <- num
                            temp
                    }
                    Short (shortScale)
            |(_,_) -> //Long scalefactors
                //Keep this for later use
                let mutable scaleLongGr0Ch1 = null
                let mutable scaleLongGr0Ch2 = null
                
                match y.granule = 0 with
                |true -> //Granule 0
                    let (longScale:ScaleFactorsLong) = {
                        granule = y.granule
                        channel = y.channel
                        data = 
                            let temp = Array.zeroCreate 22
                            for sfb = 0 to 10 do
                                let (num,tmp) = data |> getBits2 bitoffset scaleFactorLengths.[0]
                                bitoffset <- tmp
                                temp.[sfb] <- num
                            for sfb = 11 to 20 do
                                let (num,tmp) = data |> getBits2 bitoffset scaleFactorLengths.[1]
                                bitoffset <- tmp
                                temp.[sfb] <- num
                            if y.channel = 0 
                                then scaleLongGr0Ch1 <- temp
                                else scaleLongGr0Ch2 <- temp
                            temp
                    }
                    Long (longScale)
                |false -> //Granule 1 (maybe with scalefactor reuse)
                    let (longScale:ScaleFactorsLong) = {
                        granule = y.granule
                        channel = y.channel
                        data = 
                            let temp = Array.zeroCreate 22
                            let sb = [|5;10;15;20|]
                            let mutable index = 0
                            for i = 0 to 1 do
                                for sfb = index to sb.[i] do
                                    match sideInfoConfig.scfsi.[y.channel].[i] = 1 with
                                    |true -> 
                                        temp.[sfb] <- if y.channel = 0 
                                                        then scaleLongGr0Ch1.[sfb] 
                                                        else scaleLongGr0Ch2.[sfb]
                                    |false -> 
                                        let (num,tmp) = data |> getBits2 bitoffset scaleFactorLengths.[0]
                                        bitoffset <- tmp
                                        temp.[sfb] <- num
                                    index <- sfb
                                index <- index + 1
                            
                            for i = 2 to 3 do
                                for sfb = index to sb.[i] do
                                    match sideInfoConfig.scfsi.[y.channel].[i] = 1 with
                                    |true -> 
                                        temp.[sfb] <- if y.channel = 0 
                                                        then scaleLongGr0Ch1.[sfb] 
                                                        else scaleLongGr0Ch2.[sfb]
                                    |false -> 
                                        let (num,tmp) = data |> getBits2 bitoffset scaleFactorLengths.[1]
                                        bitoffset <- tmp
                                        temp.[sfb] <- num
                                    index <- sfb
                                index <- index + 1
                            temp
                    }
                    Long (longScale)
        (result,bitoffset)           

module Huffman = 
    let parseHuffmanData (data:array<byte>) offset maxbit (frame:FrameInfo) (granule:sideInfoGranule) = 
        
        let samples = Array.zeroCreate 576
        let mutable bitsArray = Array.concat [|data;Array.create 20 0uy|] //Just to be safe add padding
        let mutable bitoffset = offset
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
            let mutable temp = 0
            let mutable tempsize = 0
            let mutable colindex = 0
            
            match (snd x) with
            |0 -> (0,0,0,snd x) //Sample = 0 for table0
            |_ -> 
                let rec checkInTable (table:((int * int) array List)) rowindex = 
                    match table with
                    |[] -> (0,0,0,0)
                    |head::tail -> 
                        colindex <- -1
                        if Array.exists 
                            (fun (value,size) -> 
                                (temp <- fst ((getBits2 bitoffset size bitsArray));colindex <- colindex + 1;tempsize <- size;value = temp)) head
                            then
                                (tempsize,rowindex,colindex,snd x)
                            else
                                checkInTable tail (rowindex + 1)

                let (size,row,col,num) = checkInTable (fst x) 0
                bitoffset <- bitoffset + size
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
                            let (bits,temp) = getBits2 bitoffset bigValueLinbit.[num] bitsArray
                            bitoffset <- temp
                            bits
                        |(_,_) -> 0
                    let sign = 
                        if value > 0 
                            then 
                                let (bits,temp) = getBits2 bitoffset 1 bitsArray
                                bitoffset <- temp
                                if bits = 1 then -1 else 1
                            else
                                1
                    sign * (value + linbit)
                [row;col] |> List.map (extendSample num)
        
        //QuadTables
        let rec getQuadValues x = 
            match((bitoffset < maxbit) && ((samplecount + 4) < 576)) with
            |false -> []
            |true -> 
                let quadvalues = 
                    match granule.count1TableSelect = 1 with
                    |true -> //Get 4 bits and flip them
                        let bits = Array.toList bitsArray.[bitoffset..bitoffset+3]
                        bitoffset <- bitoffset + 4
                        bits |> List.map (fun x -> if x = 0uy then 1 else 0)
                    |false -> 
                        let rec getvalues x = 
                            match x with
                            |[] -> (0,[0;0;0;0])
                            |((hcode,size),value)::tail -> 
                                if hcode = ((getBits32 bitoffset size bitsArray) |> fst |> int)
                                    then (size,value)
                                    else getvalues tail
                        let (size,values) = quadTable |> getvalues
                        bitoffset <- bitoffset + size
                        values
                let signs = 
                    let count1 = quadvalues |> List.fold (fun acc x -> acc + x) 0
                    let bits2 = Array.toList bitsArray.[bitoffset..bitoffset + count1 - 1]
                    let mutable i = -1
                    let bits = quadvalues |> List.map (fun x -> 
                                                            let res = 
                                                                if x = 0 
                                                                    then 0 
                                                                    else i <- i + 1;bits2.[i] |> int
                                                            res)
                    bitoffset <- bitoffset + count1
                    bits
                    //Can do this too
                    (*let bits = Array.toList bitsArray.[bitoffset..bitoffset+3]
                    bitoffset < bitoffset + 4
                    bits*)
                let result = 
                    signs 
                    |> List.map int
                    |> List.zip quadvalues 
                    |> List.map (fun (x,y) -> (if y = 1 then -x else x))
                samplecount <- samplecount + 4
                result @ getQuadValues (x + 4)

        //Decode huffman tables
        let limit = granule.bigValues * 2
        while samplecount < (limit) do
            let result = samplecount |> (getTable >> decodeTable >> getSample)
            samples.[samplecount] <- result.[0]
            samples.[samplecount + 1] <- result.[1]
            samplecount <- samplecount + 2
        
        //Decode Quad Values table if applicable
        let temp = samplecount
        let quadSamples = getQuadValues 0
        match (temp < samplecount) with
        |false -> 
            samples
        |true ->
            for index = 0 to (samplecount - temp - 1) do
                samples.[temp + index] <- quadSamples.[index]
            samples

module Maindata = 
    
    open ScaleFactors
    open Huffman
    
    //Parse Main data from frame
    let parseMainData (data:array<byte>) (header:HeaderConfig) (frameinfo:FrameInfo) (sideinfo:SideInfoConfig) = 
        let arrayBits = data |> getBitsArrayfromByteArray
        let mutable bitcount = 0
        let channels = if header.channelMode = 3uy then 1 else 2

        //Create Arrays to store scalefactors and samples
        let (sclfactors,samples) = 
            match channels with
            |1 -> 
                let (sclfactors:array<ScaleFactors>) = Array.zeroCreate 2
                let (samples:array<array<int>>) = Array.zeroCreate 2
                (sclfactors,samples)
            |_ -> 
                let (sclfactors:array<ScaleFactors>) = Array.zeroCreate 4
                let (samples:array<array<int>>) = Array.zeroCreate 4
                (sclfactors,samples)

        for i = 0 to (if channels = 1 then 1 else 3) do
            let maxbit = bitcount + sideinfo.sideInfoGr.[i].par23Length
            let (x,y) = parseScaleFactors arrayBits.[bitcount..] sideinfo sideinfo.sideInfoGr.[i]
            sclfactors.[i] <- x
            bitcount <- bitcount + y
            samples.[i] <- parseHuffmanData arrayBits bitcount (sideinfo.sideInfoGr.[i].par23Length + bitcount - y) frameinfo sideinfo.sideInfoGr.[i]
            bitcount <- maxbit
        
        (sclfactors,samples)
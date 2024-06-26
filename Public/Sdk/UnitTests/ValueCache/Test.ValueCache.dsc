// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import {Assert, Testing} from "Sdk.Testing";
import * as ValueCache from "Sdk.ValueCache";

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function TwoPipsShouldGetCreated(){
        let value1 = ValueCache.getOrAdd('myKey', () => {
            return {
                result: Transformer.writeAllText({
                    outputPath: p`out/out1.txt`, 
                    text: "FileContent"
                }),
            };
        });

        let value2 = ValueCache.getOrAdd('myKey', () => {
            return {
                result: Transformer.writeAllText({
                    outputPath: p`out/out1.txt`, 
                    text: "FileContent"
                }),
            };
        });

        let value3 = ValueCache.getOrAdd('myKey2', () => {
            return {
                result: Transformer.writeAllText({
                    outputPath: p`out/out2.txt`, 
                    text: "FileContent"
                }),
            };
        });

        Assert.notEqual(value1, value2);
        Assert.notEqual(value1, value3);
        Assert.notEqual(value2, value3);

        Assert.areEqual(value1.result, value2.result);
        Assert.notEqual(value1.result, value3.result);

        Assert.areEqual(a`out1.txt`, value1.result.path.name);
        Assert.areEqual(a`out1.txt`, value2.result.path.name);
        Assert.areEqual(a`out2.txt`, value3.result.path.name);
    }

    @@Testing.unitTest()
    export function sealDirectory(){
        // Write some files to test
        let f1a = Transformer.writeAllText({
            outputPath: p`out/Dir1/a.txt`,
            text: "1A"
        });
        let f2a = Transformer.writeAllText({
            outputPath: p`out/Dir2/a.txt`,
            text: "2A"
        });
        let f2b = Transformer.writeAllText({
            outputPath: p`out/Dir2/b.txt`,
            text: "2B"
        });

        // This test relies on a small hole where we don't validate that the function is idental on each cache lookup
        // Allowing a loophool for cache inspection.
        // We need to add a test-option to diable this validation when we close that loophole.

        let full1 = Transformer.sealDirectory({
            root: d`out/Dir1`, 
            files: [f1a]
        });
        getValueFromCache(full1, "full1");

        // Each sealed directory is unique
        let full2 = Transformer.sealDirectory({
            root: d`out/Dir1`, 
            files: [f1a]
        });
        getValueFromCache(full2, "full2");

        // Partial is different from full seal
        let partial1 = Transformer.sealPartialDirectory(d`out/Dir2`, [f2a]);
        getValueFromCache(partial1, "partial1");

        // Partial seal with differnt content is different
        let partial2 = Transformer.sealPartialDirectory(d`out/Dir2`, [f2a, f2b]);
        getValueFromCache(partial2, "partial2");

        // Partial seal with same content is different
        let partial3 = Transformer.sealPartialDirectory(d`out/Dir2`, [f2a, f2b]);
        getValueFromCache(partial3, "partial3");
    }

    function getValueFromCache(directory: StaticDirectory, testName: string) {
        getValueFromCacheSingle(directory, testName);
        getValueFromCacheSingle(directory, testName);
    }

    function getValueFromCacheSingle(directory: StaticDirectory, testName: string) {
        const _ = ValueCache.getOrAdd(directory, () => {
            return {
                result: Transformer.writeAllText({
                    outputPath: p`out/temp/${testName + ".txt"}`, 
                    text: testName
                }),
            };
        });
    }
}

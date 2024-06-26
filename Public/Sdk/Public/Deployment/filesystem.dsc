// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

/**
 * Result of deploying to disk.
 */
@@public
export interface OnDiskDeployment {
    /** Input data that was used for deployment */
    deployedDefinition: Definition;

    /** Static (sealed) directory that contains all the deployed files */
    contents: StaticDirectory;

    /** Optional primary file, i.e. an executable or test dll */
    primaryFile?: File;

    /** Optional opaque directories robocopied/rsynced into this deployment */
    targetOpaques?: OpaqueDirectory[];
}

@@public
export interface OpaqueSubDirectory {
    /** Parent opaque directory */
    opaque: OpaqueDirectory,

    /** An optional path relative to that opaque directory designating a subdirectory to be added to the deployment.
     * If not specified, the whole opaque directory will be added to the deployment
    */
    subDirectory?: RelativePath,
}

/**
 * Used to represent deployment of a subdirectory of an opaque directory.
 *
 * Call the 'createDeployableOpaqueSubDirectory' function below to create an instance of this type.
 */
@@public
export interface DeployableOpaqueSubDirectory extends Deployable, OpaqueSubDirectory {
    /** This property is set automatically by the 'createDeployableOpaqueSubDirectory' function below */
    deploy: FlattenForDeploymentFunction
}

/**
 * Arguments to fine tune how things are deployed to disk
 */
@@public
export interface DeployToDiskArguments {
    /** The deployment definition to lay out on disk */
    definition: Definition;

    /** The target location where the deployment definition should be deployed to. */
    targetDirectory: Directory;

    /** Optional primary file for the resulting deployment. i.e. executable or test file. */
    primaryFile?: PathFragment;

    /** Optional list of tags to tag the pips with. */
    tags?: string[];

    /** Whether the deployment should not be fully sealed and therefore not be scrubbed. This means that old files no longer part of the build will not be removed between builds. */
    sealPartialWithoutScrubbing?: boolean;

    /** A set of options specific to the deployment. deployToDisk just dumbly passes it along to the flatten method of the Deployable interface. */
    deploymentOptions?: DeploymentOptions;

    /** Whether the deployment should be kept writable. Turning this on also has the side effect of avoiding hardlinks. */
    keepOutputsWritable?: boolean;
}

@@public
export function createDeployableOpaqueSubDirectory(opaque: OpaqueDirectory, sub?: RelativePath): Deployable {
    return <DeployableOpaqueSubDirectory> {
        opaque: opaque,
        subDirectory: sub,
        deploy: (
            item: Object, 
            targetFolder: RelativePath,
            handleDuplicateFile: HandleDuplicateFileDeployment, 
            result: FlattenedResult,
            deploymentOptions?: Object,
            provenance?: Diagnostics.Provenance) => 
        {
            const existingOpaque = result.flattenedOpaques.get(targetFolder);

            if (existingOpaque !== undefined) {
                if (!(existingOpaque.opaque === opaque && existingOpaque.subDirectory === sub)) {
                    let subDir = sub || r`.`;
                    Contract.fail(`Duplicate opaque directory. Can't deploy both '${existingOpaque.opaque.root}/${subDir}' and '${opaque.root}/${subDir}' to '${targetFolder}'`);
                }

                return result;
            }
            else {
                return {
                    flattenedFiles: result.flattenedFiles,
                    flattenedOpaques: result.flattenedOpaques.add(targetFolder, {opaque: <OpaqueDirectory>opaque, subDirectory: sub}),
                    visitedItems: result.visitedItems.add(d`{opaque.root}/${sub}`),
                };
            }
        }
    };
}

/**
 * Schedules a platform-specific process to copy file from 'source' to 'target'.
 */
@@public
export function copyFileProcess(source: Artifact, target: Artifact, dependencies: Transformer.InputArtifact[], outputs: Transformer.Output[]): Transformer.ExecuteResult {
    const wd = Context.getNewOutputDirectory("cp");
    const args: Transformer.ExecuteArguments = Context.getCurrentHost().os === "win"
        ? <Transformer.ExecuteArguments>{
            tool: {
                exe: f`${Context.getMount("Windows").path}/System32/cmd.exe`,
                dependsOnWindowsDirectories: true,
                description: "Copy File",
            },
            workingDirectory: d`${wd}`,
            arguments: [
                Cmd.argument("/D"),
                Cmd.argument("/C"),
                Cmd.argument("copy"),
                Cmd.argument("/Y"),
                Cmd.argument("/V"),
                Cmd.argument(source),
                Cmd.argument(target)
            ],
            dependencies: dependencies,
            outputs: outputs
        }
        : <Transformer.ExecuteArguments>{
            tool: {
                exe: f`/bin/cp`,
                description: "Copy File",
                dependsOnCurrentHostOSDirectories: true,
                prepareTempDirectory: true
            },
            workingDirectory: d`${wd}`,
            arguments: [
                Cmd.argument("-f"),
                Cmd.argument(source),
                Cmd.argument(target)
            ],
            dependencies: dependencies,
            outputs: outputs
        };

    return Transformer.execute(args);
}

/**
 * Schedules a platform-specific process to copy file from 'source' to 'target'.  This process takes a dependency 
 * on 'sourceOpaqueDir`, which should be the parent opaque directory containing the file 'source'.
 */
@@public
export function copyFileFromOpaqueDirectory(source: Path, target: Path, sourceOpaqueDir: OpaqueDirectory): DerivedFile {
    Contract.requires(source.isWithin(sourceOpaqueDir), "Source path must be within the source opaque directory");
    return copyFileProcess(
        /*source*/ Artifact.none(source),
        /*target*/ Artifact.output(target),
        /*dependencies*/ [ sourceOpaqueDir ],
        /*outputs*/ []
    ).getOutputFile(target);
}

/**
 * Schedules a platform-specific process to copy file from 'source' to 'target'.  This process takes a dependency 
 * on 'source' and produces a shared opaque directory at the same path as 'targetOpaqueDir'.
 */
@@public
export function copyFileIntoSharedOpaqueDirectory(source: File, target: Path, targetOpaqueDir: OpaqueDirectory): OpaqueDirectory {
    Contract.requires(target.isWithin(targetOpaqueDir), "Target path must be within the target opaque directory");
    return copyFileProcess(
        /*source*/ Artifact.input(source),
        /*target*/ Artifact.none(target),
        /*dependencies*/ [ ],
        /*outputs*/ [{ directory: targetOpaqueDir.root, kind: "shared" }]
    ).getOutputDirectory(targetOpaqueDir.root);
}

/**
 * Based on the current platform schedules either a robocopy.exe or rsync pip to copy 'sourceDir' to 'targetDir'.
 * That pip takes a dependency on `sourceDirDep` and, optionally, on a collection of opaque directories.  
 * If 'sourceDir' is not within `sourceDirDep.root`, disallowed file accesses are almost certain to happen. opaqueDirDeps
 * allows for the case where there are opaque directories under the given root, which is sometimes the case of a deployment on disk
 */
@@public
export function copyDirectory(sourceDir: Directory, targetDir: Directory, sourceDirDep: StaticDirectory, opaqueDirDeps?: OpaqueDirectory[], keepOutputsWritable?: boolean): SharedOpaqueDirectory {
    return Transformer.copyDirectory({
        sourceDir: sourceDir,
        targetDir: targetDir,
        dependencies: [sourceDirDep, ...(opaqueDirDeps || [])],
        recursive: true,
        keepOutputsWritable: keepOutputsWritable
    });
}

/**
 * Deploys a given deployment to disk
 */
@@public
export function deployToDisk(args: DeployToDiskArguments): OnDiskDeployment {
    let rootDir = args.targetDirectory || Context.getNewOutputDirectory("deployment");

    const flattened = flatten(args.definition, undefined, args.deploymentOptions);

    const targetFiles = flattened.flattenedFiles.forEach(tuple => {
        const relativeTarget = tuple[0];
        const data = tuple[1];

        const targetPath = rootDir.combine(relativeTarget);
        // data.file can be missing, for instance, if pdbs are embedded or pdb generation is skipped.
        // So we ignore these entries completely.
        // If the source and target are the same, then we assume the file is already deployed. Otherwise, we copy it.
        return data.file.path === targetPath 
            ? data.file
            : Transformer.copyFile(data.file, targetPath, args.tags, /*description*/ undefined, args.keepOutputsWritable);
    });

    const targetOpaques = flattened.flattenedOpaques.toArray().map(tuple => {
        const relativeTarget = tuple[0];
        const opaque = tuple[1].opaque;
        const opaqueSub = tuple[1].subDirectory || r`.`;

        const targetDir = d`${rootDir}/${relativeTarget}`;
        return copyDirectory(d`${opaque}/${opaqueSub}`, targetDir, opaque, /*opaqueDirDeps*/ [], args.keepOutputsWritable);
    });

    // TODO: We lack the ability to combine files and OpaqueDirectories into a new OpaqueDirectory (unless we write a single process that would do all the copies)
    // Therefore for now we'll just copy the opaques but don't make it part of the output StaticDirectory field contents;
    // we do, however, pass those additional opaque directories along (via the 'targetOpaques' property)
    // so the caller can appropriately take dependencies on them.
    const contents = args.sealPartialWithoutScrubbing
        ? Transformer.sealPartialDirectory({
            root: rootDir,
            files: targetFiles,
            tags: args.tags
            })
        : Transformer.sealDirectory({
            root: rootDir,
            files: targetFiles,
            outputDirectories: targetOpaques,
            scrub: true,
            tags: args.tags
            })
        ;

    return {
        deployedDefinition: args.definition,
        contents: contents,
        primaryFile : args.primaryFile ? contents.getFile(args.primaryFile) : undefined,
        targetOpaques: targetOpaques
    };
}

/**
 * Creates a deployment from disk by globbing the tree and constructing a definition out of it.
 * @param sourceRoot - The root of where to start from to create the deployment
 * @param patternOrOptions - The optional pattern to pass to the glob function for files. Defaults to '*'
 * @param recursive - Optionally indicates if the deployment should be crated recursively. Defaults to true.
 *
 * Remarks: The overloaded argument is for backwards compatibility. The intent is to deprecated the explicit arguments in favor of a compound options field.
 */
@@public
export function createFromDisk(sourceRoot: Directory, patternOrOptions?: (string | CreateFromDiskOptions), recursive?: boolean) : Definition {

    // Handle overload pattern
    const options : CreateFromDiskOptions = typeof patternOrOptions === "string" ? undefined : patternOrOptions;

    // Pattern needs to check the overloaded argument. Pattern defaults to '*'
    const pattern : string = typeof patternOrOptions === "string"
        ? patternOrOptions
        : options !== undefined ? options.pattern : "*";

    // Recursive prefers the excplicit argument. Recursive defaults to true
    recursive = recursive || (options !== undefined ? options.recursive : true);

    // Skip any files under excluded directories
    if (options && options.excludeDirectories) {
        if (options.excludeDirectories.indexOf(sourceRoot) >= 0) {
            return {
                contents: [],
            };
        }
    }
    
    let content : DeployableItem[] = [];
    
    let files = glob(sourceRoot, pattern);
    if (options && options.excludeFiles) {
        files = files.filter(file => !options.excludeFiles.contains(file));
    }
    content = files;

    if (recursive) {
        let directories = globFolders(sourceRoot, "*");
        for (let directory of directories) {
            const nested = createFromDisk(directory, patternOrOptions, recursive);
            const nestedWithFolder = {
                subfolder: directory.name,
                contents: [
                    nested
                ]
            };

            content = content.push(nestedWithFolder);
        }
    }

    return {
        contents: content,
    };
}

@@public
export interface CreateFromDiskOptions {
    /** Which directories to exclude */
    excludeDirectories?: Directory[],

    /** Which files to exclude */
    excludeFiles?: Set<File>,

    /** Wildcard pattern to match in each directory. */
    pattern?: string,

    /** Whether to recurse into directories or not  */
    recursive?: boolean
}

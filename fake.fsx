#r "./packages/FAKE/tools/FakeLib.dll" 

open Fake
open Fake.Testing.NUnit3
open System.IO

let solutionFilePath = "./src/Fettle.sln"
let mode = getBuildParamOrDefault "mode" "Release"

let buildTarget() =
    !! (sprintf "./src/**/bin/%s/" mode) |> CleanDirs
    
    build (fun x -> 
        { x with Verbosity = Some MSBuildVerbosity.Quiet 
                 Properties = [ "Configuration", mode ] }) solutionFilePath 

let testTarget() =
    let testAssemblies = !! (sprintf "./src/**/bin/%s/*Tests.dll" mode)
    let nUnitParams _ = 
        {
            NUnit3Defaults with 
                Workers = Some(1) 
                Labels = LabelsLevel.Off
        }
    testAssemblies |> NUnit3 nUnitParams

let coverageTarget() =
    let nunitArgs = [
                        // Only check coverage of fettle itself, don't bother with the example projects
                        sprintf "./src/Tests/bin/%s/Fettle.Tests.dll" mode; 

                        "--trace=Off";
                        "--output=./nunit-output.log"
                    ] |> String.concat " "
    let allArgs = [ 
                    "-register:path64"; 
                    "-output:\"opencover.xml\"";
                    "-returntargetcode:1";
                    "-hideskipped:All";
                    "-skipautoprops";
                    
                    // Workaround for issue where opencover tries to cover some dependencies
                    "-filter:\"+[*]* -[*.Tests]* -[Moq*]* -[nunit.framework*]*\""; 

                    "-target:\"./packages/NUnit.ConsoleRunner/tools/nunit3-console.exe\"";
                    sprintf "-targetargs:\"%s\"" nunitArgs
                  ]
    let result = 
        ExecProcess (fun info ->
            info.FileName <- "./packages/OpenCover/tools/OpenCover.Console.exe"
            info.Arguments <- allArgs |> String.concat " "
        )(System.TimeSpan.FromMinutes 7.0)

    if result <> 0 then failwith "Test coverage via OpenCover failed or timed-out"
    
let coverageReportTarget() =
    let args = [
                    "-reports:./opencover.xml";
                    "-verbosity:Warning";
                    "-targetdir:./coverage-report";
               ]
    let result = 
        ExecProcess (fun info ->
            info.FileName <- "./packages/reportgenerator/tools/ReportGenerator.exe"
            info.Arguments <- args |> String.concat " "
        )(System.TimeSpan.FromMinutes 7.0)

    if result <> 0 then failwith "Test coverage via OpenCover failed or timed-out"
    
let watchTarget() =

    use watcher = !! "src/**/*.fs" |> WatchChanges (fun changes -> 
        printfn ">>>>> Files changed: %s" (changes |> Seq.map (fun c -> c.FullPath) |> String.concat ", ")
        
        buildTarget()
        testTarget()

        printfn ">>>>> Watching the file-system for changes..."
    )

    printfn ">>>>> Watching the file-system for changes..."
    System.Console.ReadLine() |> ignore
    watcher.Dispose() 
    
let packageTarget() =
    let buildVersion = File.ReadAllText("./VERSION")
    let outputDir = "."
    CreateDir outputDir
    NuGetPack (fun p ->
        {p with
            Version = buildVersion
            WorkingDir = "."
            Files = [ (sprintf @"src\Console\bin\%s\*.*" mode, Some "tools", None) ]
            OutputPath = outputDir
            Publish = false
        })
        "./Fettle.Console.nuspec"

Target "Build" buildTarget
Target "Test" testTarget
Target "Coverage" coverageTarget
Target "CoverageReport" coverageReportTarget
Target "Package" packageTarget

"Build" ==> "Test"
"Build" ==> "Coverage" ==> "CoverageReport"
"Test" ==> "Package" 

Target "Watch" watchTarget

RunTargetOrDefault "Test"
using MarkItDown.Cli;

var rootCommand = CliRunner.BuildCommand();
return rootCommand.Parse(args).Invoke();

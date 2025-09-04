i dont have these files 

│ ├── ConversationService.cs │ ├── IFileProcessingService.cs │ ├── FileProcessingService.cs │ ├── IAzureOpenAIService.cs │ └── AzureOpenAIService.cs

and dotnet build yields this

vs code powershell

PS C:\Users\bindrap\Documents\webApp_Net> dotnet build

dotnet : The term 'dotnet' is not recognized as the name of a cmdlet, function, script file, or operable program. Check the spelling of the name, or if a path was included, verify that the path is correct and try again.

At line:1 char:1

+ dotnet build

+ ~~~~~~

    + CategoryInfo          : ObjectNotFound: (dotnet:String) [], CommandNotFoundException

    + FullyQualifiedErrorId : CommandNotFoundException

 

PS C:\Users\bindrap\Documents\webApp_Net> 

cmd prompt vs code:

C:\Users\bindrap\Documents\webApp_Net>dotnet build

'dotnet' is not recognized as an internal or external command,

operable program or batch file.

C:\Users\bindrap\Documents\webApp_Net>

cmd prompt regular:

C:\Users\bindrap\Documents\webApp_Net>dotnet build

MSBUILD : error MSB1011: Specify which project or solution file to use because this folder contains more than one project or solution file.

C:\Users\bindrap\Documents\webApp_Net>


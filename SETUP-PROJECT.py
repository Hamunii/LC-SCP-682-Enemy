#!/usr/bin/env python
import os
import shutil
from subprocess import run
from inspect import getsourcefile
from contextlib import chdir

# This is an automated script for copying required dll files into this project.
# Supports both Windows and Linux.
#
# Also, this script got kinda out of hand towards the end. I won't waste more time on this though, it works anyways.
# Feel free to contribute to this script

class color:
   reset = '\033[0m'
   green = '\033[32m'
   yellow = '\033[93m'
   red = '\033[31m'
   lightblue = '\033[94m'
   lightcyan = '\033[96m'
   purple = '\033[35m'
   orange = '\033[33m'

def exitProgram():
   print(color.reset + 'Press Enter to close the program...')
   input()
   exit()

def copyDLLs(sourceDir: str, destinationDir: str, dllList: list) -> bool:
   if os.path.exists(fr'{sourceDir}'):
      for dllFile in dllList:
         shutil.copy2(f'{sourceDir}/{dllFile}', f'{destinationDir}/{dllFile}')
         print(f'Got: {dllFile}')
      return True
   return False

def main():
   # Locate us
   thisPath = os.path.dirname(os.path.realpath(getsourcefile(lambda:0)))
   gameDataRelative = 'Lethal Company_Data/Managed'

   # Locate the game's data folder
   gameFilesPath = None
   expectedGamePaths = ['C:/Program Files (x86)/Steam/steamapps/common/Lethal Company', f'{os.path.expanduser("~")}/.local/share/Steam/steamapps/common/Lethal Company']
   for path in expectedGamePaths:
      if os.path.exists(path):
         gameFilesPath = path
         break

   if gameFilesPath is None:
      print(color.yellow + "Could not locate Lethal Company game files!\nPlease paste the full path to 'Lethal Company':" + color.reset)
      userInputGamePath = input()
      if os.path.exists(userInputGamePath):
         gameFilesPath = userInputGamePath
      else:
         print(color.red + "Could not find location.")
         exitProgram()

   if (gameFilesPath[-1] == "/" or gameFilesPath[-1] == '\\'):
      userInputPath = f'{gameFilesPath}'

   print(color.green + f'Game data path found: {gameFilesPath}' + color.reset)

   # Make sure our Unity project still exists
   unityProjectPath = f'{thisPath}/UnityProject'
   unityPluginsRelative = 'Assets/Plugins'
   if not os.path.exists(unityProjectPath):
      print(color.yellow + f'Could not find Unity project at {unityProjectPath}! Paste the full path to your Unity project:' + color.reset)
      userInputUnityPath = input()
      if os.path.exists(userInputUnityPath):
         unityProjectPath = userInputUnityPath
      else:
         print(color.red + "Could not find location.")
         exitProgram()

   if not os.path.exists(f'{unityProjectPath}/{unityPluginsRelative}'):
      print(color.red + f"Your Unity Project does not have a {unityPluginsRelative} folder!\nMake sure your Unity Project is based on Lethal Company files.")
      exitProgram()
   print(color.lightblue + f'Unity Plugins path found: {unityProjectPath}/{unityPluginsRelative}' + color.reset)

   # Copying dlls for Unity project
   print('Copying game DLLs for Unity project:')
   neededPluginDllFiles =[
      "AmazingAssets.TerrainToMesh.dll",
      "ClientNetworkTransform.dll",
      "DissonanceVoip.dll",
      "Facepunch Transport for Netcode for GameObjects.dll",
      "Facepunch.Steamworks.Win64.dll",
      "Newtonsoft.Json.dll",
      "Assembly-CSharp-firstpass.dll"
   ]
   copyDLLs(f'{gameFilesPath}/{gameDataRelative}', f'{unityProjectPath}/{unityPluginsRelative}', neededPluginDllFiles)

   print(color.green + f'Done copying game DLLs to {unityProjectPath}/{unityPluginsRelative}!' + color.reset)

   #######################################################################################
   # Run `dotnet tool restore`

   print(color.lightblue + f'Part 1 of 3 complete!{color.purple}\nRunning `dotnet tool restore`')
   with chdir(f'{thisPath}/Plugin/'):
      try:
         print(color.lightcyan + f'We are in: {os.getcwd()}{color.purple}')
         run(["dotnet", "tool", "restore"]) 
      except:
         print(color.red + f'Error: failed to run command.')

   #######################################################################################
   # Generate .csproj.user file

   print(color.lightblue + f'Part 2 of 3 complete!{color.purple}\n> Next you will have to provide a path to where we will copy your mod files each time your build it.\n'
         
         f'{color.orange}Examples:\n'
         '     r2modman: /home/user/.config/r2modmanPlus-local/LethalCompany/profiles/testing/BepInEx/scripts\n'
         '     Game installation: /home/user/.local/share/Steam/steamapps/common/Lethal Company/BepInEx/scripts')
   print(color.lightcyan + f'Paste your path: ', end='')

   userInputPath = input()
   if not os.path.exists(userInputPath):
      print(color.red + 'Path not found!')
      exitProgram()

   if not (userInputPath[-1] == "/" and not userInputPath[-1] == '\\'):
      userInputPath = f'{userInputPath}/'

   userFile = f"""<?xml version="1.0" encoding="utf-8"?>
   <Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
      <!-- GENERATED BY SETUP-PROJECT.py -->
      <PropertyGroup>
         <!-- Automatically found or manually inputted game path -->
         <GameDirectory>{gameFilesPath}/</GameDirectory>
         <!-- The path you pasted when running the script -->
         <PluginsDirectory>{userInputPath}</PluginsDirectory>
         <TestingDirectory>$(PluginsDirectory)../scripts/</TestingDirectory>
      </PropertyGroup>

      <!-- Constant Variables - Do Not modify -->
      <PropertyGroup>
         <ManagedDirectory>$(GameDirectory)Lethal Company_Data/Managed/</ManagedDirectory>
         <MMHOOK>$(PluginsDirectory)MMHOOK/</MMHOOK>
         <SCPAssets>$(TestingDirectory)SCP682Assets/</SCPAssets>
      </PropertyGroup>

      <!-- Our mod files get copied over after NetcodePatcher has processed our DLL -->
      <Target Name="CopyToTestProfile" DependsOnTargets="NetcodePatch" AfterTargets="PostBuildEvent">
         <MakeDir
            Directories="$(SCPAssets)"
            Condition="!Exists('$(SCPAssets)')"
         />
         <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(TestingDirectory)"/>
         <!-- We will copy the asset bundle named "modassets" over -->
         <Copy SourceFiles="../../UnityProject/AssetBundles/StandaloneWindows/scp682assets" DestinationFolder="$(SCPAssets)" SkipUnchangedFiles="true"/>
         <Copy SourceFiles="../../ExternalAssets/SCP682VideoBundle/scp682videobundle" DestinationFolder="$(SCPAssets)" SkipUnchangedFiles="true"/>
         <Exec Command="echo '[csproj.user] Mod files copied to $(TestingDirectory)'" />
      </Target>
   </Project>"""

   fp = open(f'{thisPath}/Plugin/SCP682.csproj.user', 'w')
   fp.write(userFile)
   fp.close()
   print(color.green + f'csproj.user file created at {thisPath}/Plugin/SCP682.csproj.user!')

   print(color.lightblue + f'Project Setup Complete!{color.lightcyan}\n> You should now be able to build the C# project, including the Asset Bundle!')
   exitProgram()

if __name__ == "__main__":
   try:
      main()
   except Exception as exc:
      print(color.red + "Something went wrong, and the setup script crashed!" + color.reset)
      print(color.red + f"The error:\n{exc}" + color.reset)
      print(color.yellow + "Make sure you run this script from the command line, like so: python SETUP-SCRIPT.py" + color.reset)
      exitProgram()
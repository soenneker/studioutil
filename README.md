# <img src="src/StudioUtil/Resources/logo.png" width=30> StudioUtil

A Visual Studio extension to ease development, with miscellaneous tools like fluent file cloning.

Download from the [Marketplace](https://marketplace.visualstudio.com/items?itemName=soenneker.StudioUtil), or from within `Visual Studio -> Extensions -> Manage Extensions`

<hr>

## Features

### **Copy a file and replace the contents**

<img src="misc/whatitdoes.gif"/>

Simply right click in your Solution explorer the file you wish to 'clone and replace':

![Context menu](misc/contextmenu.png)

It will bring up a dialog:

![clone and replace](misc/cloneandreplace.png)

Enter the `target` (what you wish to replace), and `replacement` (what you will replace `target` with).

After executing, it will create new file replacing the target's file name next to the original. It will replace all case sensitive instances as well as Camel case instances of the string within the document.

<hr>

### **Set variables**

Save the `target` and `replacement` within the extension so you don't need to constantly enter those fields if doing heavy cloning.

## Notes

*This continues to be a work in progress; collaborators are welcome.*
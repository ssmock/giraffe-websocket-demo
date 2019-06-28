This project demonstrates some simple web socket operations. It uses advice from
[David Sinclair's 2017 article](https://medium.com/@dsincl12/websockets-with-f-and-giraffe-772be829e121)
as a basis, but has been expanded largely through my own experimentation. This implementation might not
be performant or idiomatic, but it generally seems to work okay.

# GiraffeRazorWebsockets (remarks below from the Giraffe project template)

A [Giraffe](https://github.com/giraffe-fsharp/Giraffe) web application, which has been created via the `dotnet new giraffe` command.

## Build and test the application

### Windows

Run the `build.bat` script in order to restore, build and test (if you've selected to include tests) the application:

```
> ./build.bat
```

### Linux/macOS

Run the `build.sh` script in order to restore, build and test (if you've selected to include tests) the application:

```
$ ./build.sh
```

## Run the application

After a successful build you can start the web application by executing the following command in your terminal:

```
dotnet run src/GiraffeRazorWebsockets
```

After the application has started visit [http://localhost:5000](http://localhost:5000) in your preferred browser.
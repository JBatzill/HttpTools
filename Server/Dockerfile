#Depending on the operating system of the host machines(s) that will build or run the containers, the image specified in the FROM statement may need to be changed.
#For more information, please see http://aka.ms/containercompat 

FROM mcr.microsoft.com/dotnet/framework/runtime:4.7.2-windowsservercore-ltsc2019
ARG source
WORKDIR /app
COPY ${source:-obj/Docker/publish} .
EXPOSE 10000 10009
ENTRYPOINT ["C:\\app\\Batzill.Server.exe"]

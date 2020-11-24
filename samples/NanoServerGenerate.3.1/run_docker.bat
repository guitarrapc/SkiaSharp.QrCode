pushd ..\..\
docker build -t sample3.1 -f .\sample\NanoServerGenerate.3.1\Dockerfile .
docker run -it --rm sample3.1
popd
pause
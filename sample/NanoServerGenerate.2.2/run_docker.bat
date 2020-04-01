pushd ..\..\
docker build -t sample2.2 -f .\sample\NanoServerGenerate.2.2\Dockerfile .
docker run -it --rm sample2.2
popd
pause
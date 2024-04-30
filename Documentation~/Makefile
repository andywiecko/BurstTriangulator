install:
	dotnet tool update -g docfx

copy: 
	cp ../CHANGELOG.md changelog/
	cp ../LICENSE.md license/

build: copy
	docfx docfx.json
	docfx build docfx.json

serve: copy
	docfx docfx.json --serve

clean:
	rm -Rf _site/
	rm -f changelog/CHANGELOG.md
	rm -f license/LICENSE.md
	rm -f api/*.yml
	rm -f api/.manifest

# Get object

GET https://example.developer.anduril.com/api/v1/objects/{objectPath}

Fetches an object from your environment using the objectPath path parameter.

Reference: https://developer.anduril.com/reference/rest/objects/get-object

## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/objects/{objectPath}:
    get:
      operationId: get-object
      summary: Get object
      description: >-
        Fetches an object from your environment using the objectPath path
        parameter.
      tags:
        - subpackage_objects
      parameters:
        - name: objectPath
          in: path
          description: The path of the object to fetch.
          required: true
          schema:
            type: string
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
        - name: Accept-Encoding
          in: header
          description: >-
            If set, Lattice will compress the response using the specified
            compression method. If the header is not defined, or the compression
            method is set to `identity`, no compression will be applied to the
            response.
          required: false
          schema:
            $ref: >-
              #/components/schemas/ApiV1ObjectsObjectPathGetParametersAcceptEncoding
        - name: Priority
          in: header
          description: >
            Indicates a client's preference for the priority of the response.
            The value is a structured header as defined in RFC 9218. If you do
            not set the header, Lattice uses the default priority set for the
            environment. Incremental delivery directives are not supported and
            will be ignored.
          required: false
          schema:
            type: string
      responses:
        '200':
          description: Successful operation
          content:
            application/octet-stream:
              schema:
                type: string
                format: binary
        '400':
          description: Bad request
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
        '401':
          description: Unauthorized to access resource
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
        '404':
          description: The specified resource was not found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
        '500':
          description: Internal server error
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
servers:
  - url: https://example.developer.anduril.com
components:
  schemas:
    ApiV1ObjectsObjectPathGetParametersAcceptEncoding:
      type: string
      enum:
        - identity
        - zstd
      title: ApiV1ObjectsObjectPathGetParametersAcceptEncoding
    Error:
      type: object
      properties:
        code:
          type: string
        message:
          type: string
      required:
        - code
        - message
      title: Error
  securitySchemes:
    OAuth:
      type: http
      scheme: bearer

```

## SDK Code Examples

```python
import requests

url = "https://example.developer.anduril.com/api/v1/objects/objectPath"

headers = {"Authorization": "Bearer <token>"}

response = requests.get(url, headers=headers)

print(response.json())
```

```javascript
const url = 'https://example.developer.anduril.com/api/v1/objects/objectPath';
const options = {method: 'GET', headers: {Authorization: 'Bearer <token>'}};

try {
  const response = await fetch(url, options);
  const data = await response.json();
  console.log(data);
} catch (error) {
  console.error(error);
}
```

```go
package main

import (
	"fmt"
	"net/http"
	"io"
)

func main() {

	url := "https://example.developer.anduril.com/api/v1/objects/objectPath"

	req, _ := http.NewRequest("GET", url, nil)

	req.Header.Add("Authorization", "Bearer <token>")

	res, _ := http.DefaultClient.Do(req)

	defer res.Body.Close()
	body, _ := io.ReadAll(res.Body)

	fmt.Println(res)
	fmt.Println(string(body))

}
```

```ruby
require 'uri'
require 'net/http'

url = URI("https://example.developer.anduril.com/api/v1/objects/objectPath")

http = Net::HTTP.new(url.host, url.port)
http.use_ssl = true

request = Net::HTTP::Get.new(url)
request["Authorization"] = 'Bearer <token>'

response = http.request(request)
puts response.read_body
```

```java
import com.mashape.unirest.http.HttpResponse;
import com.mashape.unirest.http.Unirest;

HttpResponse<String> response = Unirest.get("https://example.developer.anduril.com/api/v1/objects/objectPath")
  .header("Authorization", "Bearer <token>")
  .asString();
```

```php
<?php
require_once('vendor/autoload.php');

$client = new \GuzzleHttp\Client();

$response = $client->request('GET', 'https://example.developer.anduril.com/api/v1/objects/objectPath', [
  'headers' => [
    'Authorization' => 'Bearer <token>',
  ],
]);

echo $response->getBody();
```

```csharp
using RestSharp;

var client = new RestClient("https://example.developer.anduril.com/api/v1/objects/objectPath");
var request = new RestRequest(Method.GET);
request.AddHeader("Authorization", "Bearer <token>");
IRestResponse response = client.Execute(request);
```

```swift
import Foundation

let headers = ["Authorization": "Bearer <token>"]

let request = NSMutableURLRequest(url: NSURL(string: "https://example.developer.anduril.com/api/v1/objects/objectPath")! as URL,
                                        cachePolicy: .useProtocolCachePolicy,
                                    timeoutInterval: 10.0)
request.httpMethod = "GET"
request.allHTTPHeaderFields = headers

let session = URLSession.shared
let dataTask = session.dataTask(with: request as URLRequest, completionHandler: { (data, response, error) -> Void in
  if (error != nil) {
    print(error as Any)
  } else {
    let httpResponse = response as? HTTPURLResponse
    print(httpResponse)
  }
})

dataTask.resume()
```
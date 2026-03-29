# List objects

GET https://example.developer.anduril.com/api/v1/objects

Lists objects in your environment. You can define a prefix to list a subset of your objects. If you do not set a prefix, Lattice returns all available objects. By default this endpoint will list local objects only.

Reference: https://developer.anduril.com/reference/rest/objects/list-objects

## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/objects:
    get:
      operationId: list-objects
      summary: List objects
      description: >-
        Lists objects in your environment. You can define a prefix to list a
        subset of your objects. If you do not set a prefix, Lattice returns all
        available objects. By default this endpoint will list local objects
        only.
      tags:
        - subpackage_objects
      parameters:
        - name: prefix
          in: query
          description: >-
            Filters the objects based on the specified prefix path. If no path
            is specified, all objects are returned.
          required: false
          schema:
            type: string
        - name: sinceTimestamp
          in: query
          description: Sets the age for the oldest objects to query across the environment.
          required: false
          schema:
            type: string
            format: date-time
        - name: pageToken
          in: query
          description: >-
            Base64 and URL-encoded cursor returned by the service to continue
            paging.
          required: false
          schema:
            type: string
            format: string
        - name: allObjectsInMesh
          in: query
          description: Lists objects across all environment nodes in a Lattice Mesh.
          required: false
          schema:
            type: boolean
        - name: maxPageSize
          in: query
          description: >-
            Sets the maximum number of items that should be returned on a single
            page.
          required: false
          schema:
            type: integer
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '200':
          description: Successful operation
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListResponse'
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
    ContentIdentifier:
      type: object
      properties:
        path:
          type: string
          description: >
            A valid path must not contain the following:

            - Spaces or Tabs

            - Special characters other than underscore (_), dash (-), period (.)
            and slash (/)

            - Non-ASCII characters such as accents or symbols

            Paths must not start with a leading space.
        checksum:
          type: string
          description: The SHA-256 checksum of this object.
      required:
        - path
        - checksum
      title: ContentIdentifier
    PathMetadata:
      type: object
      properties:
        content_identifier:
          $ref: '#/components/schemas/ContentIdentifier'
        size_bytes:
          type: integer
          format: uint64
        last_updated_at:
          type: string
          format: date-time
        expiry_time:
          type: string
          format: date-time
      required:
        - content_identifier
        - size_bytes
        - last_updated_at
      title: PathMetadata
    ListResponse:
      type: object
      properties:
        path_metadatas:
          type: array
          items:
            $ref: '#/components/schemas/PathMetadata'
        next_page_token:
          type: string
      required:
        - path_metadatas
      title: ListResponse
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

url = "https://example.developer.anduril.com/api/v1/objects"

headers = {"Authorization": "Bearer <token>"}

response = requests.get(url, headers=headers)

print(response.json())
```

```javascript
const url = 'https://example.developer.anduril.com/api/v1/objects';
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

	url := "https://example.developer.anduril.com/api/v1/objects"

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

url = URI("https://example.developer.anduril.com/api/v1/objects")

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

HttpResponse<String> response = Unirest.get("https://example.developer.anduril.com/api/v1/objects")
  .header("Authorization", "Bearer <token>")
  .asString();
```

```php
<?php
require_once('vendor/autoload.php');

$client = new \GuzzleHttp\Client();

$response = $client->request('GET', 'https://example.developer.anduril.com/api/v1/objects', [
  'headers' => [
    'Authorization' => 'Bearer <token>',
  ],
]);

echo $response->getBody();
```

```csharp
using RestSharp;

var client = new RestClient("https://example.developer.anduril.com/api/v1/objects");
var request = new RestRequest(Method.GET);
request.AddHeader("Authorization", "Bearer <token>");
IRestResponse response = client.Execute(request);
```

```swift
import Foundation

let headers = ["Authorization": "Bearer <token>"]

let request = NSMutableURLRequest(url: NSURL(string: "https://example.developer.anduril.com/api/v1/objects")! as URL,
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
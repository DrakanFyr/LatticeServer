# Upload object

POST https://example.developer.anduril.com/api/v1/objects/{objectPath}
Content-Type: application/octet-stream

Uploads an object. The object must be 1 GiB or smaller.

Reference: https://developer.anduril.com/reference/rest/objects/upload-object

## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/objects/{objectPath}:
    post:
      operationId: upload-object
      summary: Upload object
      description: Uploads an object. The object must be 1 GiB or smaller.
      tags:
        - subpackage_objects
      parameters:
        - name: objectPath
          in: path
          description: Path of the Object that is to be uploaded.
          required: true
          schema:
            type: string
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
        - name: Time-To-Live
          in: header
          description: >-
            An optional expiry TTL associated with an object. The value
            represents the number of nanoseconds the object will exist in the
            local store. If no TTL is supplied, the server applies a default
            TTL. In most cases, the default TTL is 90 days. However, it might be
            higher or lower, depending on the retention requirements of your
            environment.
          required: false
          schema:
            type: integer
            format: int64
      responses:
        '200':
          description: Successful upload
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PathMetadata'
        '400':
          description: Bad request
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
        '413':
          description: Content too large
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
        '507':
          description: Insuccifient Storage
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
      requestBody:
        content:
          application/octet-stream:
            schema:
              type: string
              format: binary
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

headers = {
    "Time-To-Live": "123",
    "Authorization": "Bearer <token>",
    "Content-Type": "application/octet-stream"
}

response = requests.post(url, headers=headers)

print(response.json())
```

```javascript
const url = 'https://example.developer.anduril.com/api/v1/objects/objectPath';
const options = {
  method: 'POST',
  headers: {
    'Time-To-Live': '123',
    Authorization: 'Bearer <token>',
    'Content-Type': 'application/octet-stream'
  }
};

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

	req, _ := http.NewRequest("POST", url, nil)

	req.Header.Add("Time-To-Live", "123")
	req.Header.Add("Authorization", "Bearer <token>")
	req.Header.Add("Content-Type", "application/octet-stream")

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

request = Net::HTTP::Post.new(url)
request["Time-To-Live"] = '123'
request["Authorization"] = 'Bearer <token>'
request["Content-Type"] = 'application/octet-stream'

response = http.request(request)
puts response.read_body
```

```java
import com.mashape.unirest.http.HttpResponse;
import com.mashape.unirest.http.Unirest;

HttpResponse<String> response = Unirest.post("https://example.developer.anduril.com/api/v1/objects/objectPath")
  .header("Time-To-Live", "123")
  .header("Authorization", "Bearer <token>")
  .header("Content-Type", "application/octet-stream")
  .asString();
```

```php
<?php
require_once('vendor/autoload.php');

$client = new \GuzzleHttp\Client();

$response = $client->request('POST', 'https://example.developer.anduril.com/api/v1/objects/objectPath', [
  'headers' => [
    'Authorization' => 'Bearer <token>',
    'Content-Type' => 'application/octet-stream',
    'Time-To-Live' => '123',
  ],
]);

echo $response->getBody();
```

```csharp
using RestSharp;

var client = new RestClient("https://example.developer.anduril.com/api/v1/objects/objectPath");
var request = new RestRequest(Method.POST);
request.AddHeader("Time-To-Live", "123");
request.AddHeader("Authorization", "Bearer <token>");
request.AddHeader("Content-Type", "application/octet-stream");
IRestResponse response = client.Execute(request);
```

```swift
import Foundation

let headers = [
  "Time-To-Live": "123",
  "Authorization": "Bearer <token>",
  "Content-Type": "application/octet-stream"
]

let request = NSMutableURLRequest(url: NSURL(string: "https://example.developer.anduril.com/api/v1/objects/objectPath")! as URL,
                                        cachePolicy: .useProtocolCachePolicy,
                                    timeoutInterval: 10.0)
request.httpMethod = "POST"
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
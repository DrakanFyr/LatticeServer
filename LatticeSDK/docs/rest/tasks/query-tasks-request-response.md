## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/tasks/query:
    post:
      operationId: query-tasks
      summary: Query tasks
      description: >-
        Searches for Tasks that match specified filtering criteria and returns
        matching tasks in paginated form.


        This method allows filtering tasks based on multiple criteria including:

        - Parent task relationships

        - Task status (with inclusive or exclusive filtering)

        - Update time ranges

        - Task view (manager or agent perspective)

        - Task assignee

        - Task type (via exact URL matches or prefix matching)


        Results are returned in pages. When more results are available than can
        be returned in a single

        response, a page_token is provided that can be used in subsequent
        requests to retrieve the next

        set of results.


        By default, this returns the latest task version for each matching task
        from the manager's perspective.
      tags:
        - subpackage_tasks
      parameters:
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '200':
          description: Task query was successful
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TaskQueryResults'
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
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/TaskQuery'
servers:
  - url: https://example.developer.anduril.com

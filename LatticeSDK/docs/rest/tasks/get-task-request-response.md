## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/tasks/{taskId}:
    get:
      operationId: get-task
      summary: Get task
      description: >-
        Retrieves a specific Task by its ID, with options to select a particular
        task version or view.


        This method returns detailed information about a task including its
        current status,

        specification, relations, and other metadata. The response includes the
        complete Task object

        with all associated fields.


        By default, the method returns the latest definition version of the task
        from the manager's

        perspective.
      tags:
        - subpackage_tasks
      parameters:
        - name: taskId
          in: path
          description: ID of task to return
          required: true
          schema:
            type: string
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '200':
          description: Task retrieval was successful.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Task'
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
servers:
  - url: https://example.developer.anduril.com

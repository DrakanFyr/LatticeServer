components:
  schemas:
    EntityStreamRequest:
      type: object
      properties:
        heartbeatIntervalMS:
          type: integer
          description: at what interval to send heartbeat events, defaults to 30s.
        preExistingOnly:
          type: boolean
          description: >-
            only stream pre-existing entities in the environment and then close
            the connection, defaults to false.
        componentsToInclude:
          type: array
          items:
            type: string
          description: >-
            list of components to include, leave empty to include all
            components.
      title: EntityStreamRequest
    Timestamp:
      type: string
      description: The datetime string in ISO 8601 format.
      title: Timestamp
    EntityStreamHeartbeatEvent:
      type: string
      enum:
        - heartbeat
      title: EntityStreamHeartbeatEvent
    EntityEventEventType:
      type: string
      enum:
        - EVENT_TYPE_INVALID
        - EVENT_TYPE_CREATED
        - EVENT_TYPE_UPDATE
        - EVENT_TYPE_DELETED
        - EVENT_TYPE_PREEXISTING
        - EVENT_TYPE_POST_EXPIRY_OVERRIDE
      title: EntityEventEventType
    Status:
      type: object
      properties:
        platformActivity:
          type: string
          description: |-
            A string that describes the activity that the entity is performing.
             Examples include "RECONNAISSANCE", "INTERDICTION", "RETURN TO BASE (RTB)", "PREPARING FOR LAUNCH".
        role:
          type: string
          description: >-
            A human-readable string that describes the role the entity is
            currently performing. E.g. "Team Member", "Commander".
      description: Contains status of entities.
      title: Status
    Position:
      type: object
      properties:
        latitudeDegrees:
          type: number
          format: double
          description: WGS84 geodetic latitude in decimal degrees.
        longitudeDegrees:
          type: number
          format: double
          description: WGS84 longitude in decimal degrees.
        altitudeHaeMeters:
          type: number
          format: double
          description: >-
            altitude as height above ellipsoid (WGS84) in meters. DoubleValue
            wrapper is used to distinguish optional from
             default 0.
        altitudeAglMeters:
          type: number
          format: double
          description: >-
            Altitude as AGL (Above Ground Level) if the upstream data source has
            this value set. This value represents the
             entity's height above the terrain. This is typically measured with a radar altimeter or by using a terrain tile
             set lookup. If the value is not set from the upstream, this value is not set.
        altitudeAsfMeters:
          type: number
          format: double
          description: >-
            Altitude as ASF (Above Sea Floor) if the upstream data source has
            this value set. If the value is not set from the upstream, this
            value is
             not set.
        pressureDepthMeters:
          type: number
          format: double
          description: >-
            The depth of the entity from the surface of the water through sensor
            measurements based on differential pressure
             between the interior and exterior of the vessel. If the value is not set from the upstream, this value is not set.
      description: |-
        WGS84 position. Position includes four altitude references.
         The data model does not currently support Mean Sea Level (MSL) references,
         such as the Earth Gravitational Model 1996 (EGM-96) and the Earth Gravitational Model 2008 (EGM-08).
         If the only altitude reference available to your integration is MSL, convert it to
         Height Above Ellipsoid (HAE) and populate the altitude_hae_meters field.
      title: Position
    ENU:
      type: object
      properties:
        e:
          type: number
          format: double
        'n':
          type: number
          format: double
        u:
          type: number
          format: double
      title: ENU
    Quaternion:
      type: object
      properties:
        x:
          type: number
          format: double
          description: x, y, z are vector portion, w is scalar
        'y':
          type: number
          format: double
        z:
          type: number
          format: double
        w:
          type: number
          format: double
      title: Quaternion
    Location:
      type: object
      properties:
        position:
          $ref: '#/components/schemas/Position'
          description: see Position definition for details.
        velocityEnu:
          $ref: '#/components/schemas/ENU'
          description: >-
            Velocity in an ENU reference frame centered on the corresponding
            position. All units are meters per second.
        speedMps:
          type: number
          format: double
          description: >-
            Speed is the magnitude of velocity_enu vector [sqrt(e^2 + n^2 +
            u^2)] when present, measured in m/s.
        acceleration:
          $ref: '#/components/schemas/ENU'
          description: The entity's acceleration in meters/s^2.
        attitudeEnu:
          $ref: '#/components/schemas/Quaternion'
          description: quaternion to translate from entity body frame to it's ENU frame
      description: Available for Entities that have a single or primary Location.
      title: Location
    EntityManager.TMat3:
      type: object
      properties:
        mxx:
          type: number
          format: double
        mxy:
          type: number
          format: double
        mxz:
          type: number
          format: double
        myy:
          type: number
          format: double
        myz:
          type: number
          format: double
        mzz:
          type: number
          format: double
      description: Symmetric 3d matrix only representing the upper right triangle.
      title: EntityManager.TMat3
    ErrorEllipse:
      type: object
      properties:
        probability:
          type: number
          format: double
          description: >-
            Defines the probability in percentage that an entity lies within the
            given ellipse: 0-1.
        semiMajorAxisM:
          type: number
          format: double
          description: >-
            Defines the distance from the center point of the ellipse to the
            furthest distance on the perimeter in meters.
        semiMinorAxisM:
          type: number
          format: double
          description: >-
            Defines the distance from the center point of the ellipse to the
            shortest distance on the perimeter in meters.
        orientationD:
          type: number
          format: double
          description: >-
            The orientation of the semi-major relative to true north in degrees
            from clockwise: 0-180 due to symmetry across the semi-minor axis.
      description: >-
        Indicates ellipse characteristics and probability that an entity lies
        within the defined ellipse.
      title: ErrorEllipse
    LocationUncertainty:
      type: object
      properties:
        positionEnuCov:
          $ref: '#/components/schemas/EntityManager.TMat3'
          description: >-
            Positional covariance represented by the upper triangle of the
            covariance matrix. It is valid to populate
             only the diagonal of the matrix if the full covariance matrix is unknown.
        velocityEnuCov:
          $ref: '#/components/schemas/EntityManager.TMat3'
          description: >-
            Velocity covariance represented by the upper triangle of the
            covariance matrix. It is valid to populate
             only the diagonal of the matrix if the full covariance matrix is unknown.
        positionErrorEllipse:
          $ref: '#/components/schemas/ErrorEllipse'
          description: >-
            An ellipse that describes the certainty probability and error
            boundary for a given geolocation.
      description: Uncertainty of entity position and velocity, if available.
      title: LocationUncertainty
    GeoPoint:
      type: object
      properties:
        position:
          $ref: '#/components/schemas/Position'
      description: |-
        A point shaped geo-entity.
         See https://datatracker.ietf.org/doc/html/rfc7946#section-3.1.2
      title: GeoPoint
    GeoLine:
      type: object
      properties:
        positions:
          type: array
          items:
            $ref: '#/components/schemas/Position'
      description: |-
        A line shaped geo-entity.
         See https://datatracker.ietf.org/doc/html/rfc7946#section-3.1.4
      title: GeoLine
    GeoPolygonPosition:
      type: object
      properties:
        position:
          $ref: '#/components/schemas/Position'
          description: base position. if no altitude set, its on the ground.
        heightM:
          type: number
          format: double
          description: |-
            optional height above base position to extrude in meters.
             for a given polygon, all points should have a height or none of them.
             strictly GeoJSON compatible polygons will not have this set.
      description: A position in a GeoPolygon with an optional extruded height.
      title: GeoPolygonPosition
    LinearRing:
      type: object
      properties:
        positions:
          type: array
          items:
            $ref: '#/components/schemas/GeoPolygonPosition'
      description: A closed ring of points. The first and last point must be the same.
      title: LinearRing
    GeoPolygon:
      type: object
      properties:
        rings:
          type: array
          items:
            $ref: '#/components/schemas/LinearRing'
          description: >-
            An array of LinearRings where the first item is the exterior ring
            and subsequent items are interior rings.
        isRectangle:
          type: boolean
          description: >-
            An extension hint that this polygon is a rectangle. When true this
            implies several things:
             * exactly 1 linear ring with 5 points (starting corner, 3 other corners and start again)
             * each point has the same altitude corresponding with the plane of the rectangle
             * each point has the same height (either all present and equal, or all not present)
      description: |-
        A polygon shaped geo-entity.
         See https://datatracker.ietf.org/doc/html/rfc7946#section-3.1.6, only canonical representations accepted
      title: GeoPolygon
    GeoEllipse:
      type: object
      properties:
        semiMajorAxisM:
          type: number
          format: double
          description: >-
            Defines the distance from the center point of the ellipse to the
            furthest distance on the perimeter in meters.
        semiMinorAxisM:
          type: number
          format: double
          description: >-
            Defines the distance from the center point of the ellipse to the
            shortest distance on the perimeter in meters.
        orientationD:
          type: number
          format: double
          description: >-
            The orientation of the semi-major relative to true north in degrees
            from clockwise: 0-180 due to symmetry across the semi-minor axis.
        heightM:
          type: number
          format: double
          description: >-
            Optional height above entity position to extrude in meters. A
            non-zero value creates an elliptic cylinder
      description: |-
        An ellipse shaped geo-entity.
         For a circle, the major and minor axis would be the same values.
         This shape is NOT Geo-JSON compatible.
      title: GeoEllipse
    GeoEllipsoid:
      type: object
      properties:
        forwardAxisM:
          type: number
          format: double
          description: >-
            Defines the distance from the center point to the surface along the
            forward axis
        sideAxisM:
          type: number
          format: double
          description: >-
            Defines the distance from the center point to the surface along the
            side axis
        upAxisM:
          type: number
          format: double
          description: >-
            Defines the distance from the center point to the surface along the
            up axis
      description: |-
        An ellipsoid shaped geo-entity.
         Principal axis lengths are defined in entity body space
         This shape is NOT Geo-JSON compatible.
      title: GeoEllipsoid
    GeoShape:
      type: object
      properties:
        point:
          $ref: '#/components/schemas/GeoPoint'
        line:
          $ref: '#/components/schemas/GeoLine'
        polygon:
          $ref: '#/components/schemas/GeoPolygon'
        ellipse:
          $ref: '#/components/schemas/GeoEllipse'
        ellipsoid:
          $ref: '#/components/schemas/GeoEllipsoid'
      description: A component that describes the shape of a geo-entity.
      title: GeoShape
    GeoDetailsType:
      type: string
      enum:
        - GEO_TYPE_INVALID
        - GEO_TYPE_GENERAL
        - GEO_TYPE_HAZARD
        - GEO_TYPE_EMERGENCY
        - GEO_TYPE_ENGAGEMENT_ZONE
        - GEO_TYPE_CONTROL_AREA
        - GEO_TYPE_BULLSEYE
        - GEO_TYPE_ACM
      title: GeoDetailsType
    ControlAreaDetailsType:
      type: string
      enum:
        - CONTROL_AREA_TYPE_INVALID
        - CONTROL_AREA_TYPE_KEEP_IN_ZONE
        - CONTROL_AREA_TYPE_KEEP_OUT_ZONE
        - CONTROL_AREA_TYPE_DITCH_ZONE
        - CONTROL_AREA_TYPE_LOITER_ZONE
      title: ControlAreaDetailsType
    ControlAreaDetails:
      type: object
      properties:
        type:
          $ref: '#/components/schemas/ControlAreaDetailsType'
      description: |-
        Determines the type of control area being represented by the geo-entity,
         in which an asset can, or cannot, operate.
      title: ControlAreaDetails
    AcmDetailsAcmType:
      type: string
      enum:
        - ACM_DETAIL_TYPE_INVALID
        - ACM_DETAIL_TYPE_LANDING_ZONE
      title: AcmDetailsAcmType
    ACMDetails:
      type: object
      properties:
        acmType:
          $ref: '#/components/schemas/AcmDetailsAcmType'
        acmDescription:
          type: string
          description: >-
            Used for loosely typed associations, such as assignment to a
            specific fires unit.
             Limit to 150 characters.
      title: ACMDetails
    GeoDetails:
      type: object
      properties:
        type:
          $ref: '#/components/schemas/GeoDetailsType'
        controlArea:
          $ref: '#/components/schemas/ControlAreaDetails'
        acm:
          $ref: '#/components/schemas/ACMDetails'
      description: A component that describes a geo-entity.
      title: GeoDetails
    AlternateIdType:
      type: string
      enum:
        - ALT_ID_TYPE_INVALID
        - ALT_ID_TYPE_TRACK_ID_2
        - ALT_ID_TYPE_TRACK_ID_1
        - ALT_ID_TYPE_SPI_ID
        - ALT_ID_TYPE_NITF_FILE_TITLE
        - ALT_ID_TYPE_TRACK_REPO_ALERT_ID
        - ALT_ID_TYPE_ASSET_ID
        - ALT_ID_TYPE_LINK16_TRACK_NUMBER
        - ALT_ID_TYPE_LINK16_JU
        - ALT_ID_TYPE_NCCT_MESSAGE_ID
        - ALT_ID_TYPE_CALLSIGN
        - ALT_ID_TYPE_MMSI_ID
        - ALT_ID_TYPE_VMF_URN
        - ALT_ID_TYPE_IMO_ID
        - ALT_ID_TYPE_VMF_TARGET_NUMBER
        - ALT_ID_TYPE_SERIAL_NUMBER
        - ALT_ID_TYPE_REGISTRATION_ID
        - ALT_ID_TYPE_IBS_GID
        - ALT_ID_TYPE_DODAAC
        - ALT_ID_TYPE_UIC
        - ALT_ID_TYPE_NORAD_CAT_ID
        - ALT_ID_TYPE_UNOOSA_NAME
        - ALT_ID_TYPE_UNOOSA_ID
      title: AlternateIdType
    AlternateId:
      type: object
      properties:
        id:
          type: string
        type:
          $ref: '#/components/schemas/AlternateIdType'
      description: An alternate id for an Entity.
      title: AlternateId
    Aliases:
      type: object
      properties:
        alternateIds:
          type: array
          items:
            $ref: '#/components/schemas/AlternateId'
        name:
          type: string
          description: The best available version of the entity's display name.
      description: Available for any Entities with alternate ids in other systems.
      title: Aliases
    UInt32Range:
      type: object
      properties:
        lowerBound:
          type: integer
          format: uint
        upperBound:
          type: integer
          format: uint
      title: UInt32Range
    LlaAltitudeReference:
      type: string
      enum:
        - ALTITUDE_REFERENCE_INVALID
        - ALTITUDE_REFERENCE_HEIGHT_ABOVE_WGS84
        - ALTITUDE_REFERENCE_HEIGHT_ABOVE_EGM96
        - ALTITUDE_REFERENCE_UNKNOWN
        - ALTITUDE_REFERENCE_BAROMETRIC
        - ALTITUDE_REFERENCE_ABOVE_SEA_FLOOR
        - ALTITUDE_REFERENCE_BELOW_SEA_SURFACE
      description: |-
        Meaning of alt.
         altitude in meters above either WGS84 or EGM96, use altitude_reference to
         determine what zero means.
      title: LlaAltitudeReference
    LLA:
      type: object
      properties:
        lon:
          type: number
          format: double
        lat:
          type: number
          format: double
        alt:
          type: number
          format: double
        is2d:
          type: boolean
        altitudeReference:
          $ref: '#/components/schemas/LlaAltitudeReference'
          description: |-
            Meaning of alt.
             altitude in meters above either WGS84 or EGM96, use altitude_reference to
             determine what zero means.
      title: LLA
    Pose:
      type: object
      properties:
        pos:
          $ref: '#/components/schemas/LLA'
          description: Geospatial location defined by this Pose.
        attEnu:
          $ref: '#/components/schemas/Quaternion'
          description: >-
            The quaternion to transform a point in the Pose frame to the ENU
            frame. The Pose frame could be Body, Turret,
             etc and is determined by the context in which this Pose is used.
             The normal convention for defining orientation is to list the frames of transformation, for example
             att_gimbal_to_enu is the quaternion which transforms a point in the gimbal frame to the body frame, but
             in this case we truncate to att_enu because the Pose frame isn't defined. A potentially better name for this
             field would have been att_pose_to_enu.

             Implementations of this quaternion should left multiply this quaternion to transform a point from the Pose frame
             to the enu frame.

             Point<Pose\> posePt{1,0,0};
             Rotation<Enu, Pose\> attPoseToEnu{};
             Point<Enu\> = attPoseToEnu*posePt;

             This transformed point represents some vector in ENU space that is aligned with the x axis of the attPoseToEnu
             matrix.

             An alternative matrix expression is as follows:
             ptEnu = M x ptPose
      title: Pose
    TMat2:
      type: object
      properties:
        mxx:
          type: number
          format: double
        mxy:
          type: number
          format: double
        myy:
          type: number
          format: double
      description: >-
        symmetric 2d matrix only representing the upper right triangle, useful
        for
         covariance matrices
      title: TMat2
    AngleOfArrival:
      type: object
      properties:
        relativePose:
          $ref: '#/components/schemas/Pose'
          description: >-
            Origin (LLA) and attitude (relative to ENU) of a ray pointing
            towards the detection. The attitude represents a
             forward-left-up (FLU) frame where the x-axis (1, 0, 0) is pointing towards the target.
        bearingElevationCovarianceRad2:
          $ref: '#/components/schemas/TMat2'
          description: >-
            Bearing/elevation covariance matrix where bearing is defined in
            radians CCW+ about the z-axis from the x-axis of FLU frame
             and elevation is positive down from the FL/XY plane.
             mxx = bearing variance in rad^2
             mxy = bearing/elevation covariance in rad^2
             myy = elevation variance in rad^2
      description: The direction from which the signal is received
      title: AngleOfArrival
    Measurement:
      type: object
      properties:
        value:
          type: number
          format: double
          description: The value of the measurement.
        sigma:
          type: number
          format: double
          description: Estimated one standard deviation in same unit as the value.
      description: A component that describes some measured value with error.
      title: Measurement
    LineOfBearing:
      type: object
      properties:
        angleOfArrival:
          $ref: '#/components/schemas/AngleOfArrival'
          description: The direction pointing from this entity to the detection
        rangeEstimateM:
          $ref: '#/components/schemas/Measurement'
          description: The estimated distance of the detection
        maxRangeM:
          $ref: '#/components/schemas/Measurement'
          description: The maximum distance of the detection
      description: A line of bearing of a signal.
      title: LineOfBearing
    Tracked:
      type: object
      properties:
        trackQualityWrapper:
          type: integer
          description: Quality score, 0-15, nil if none
        sensorHits:
          type: integer
          description: Sensor hits aggregation on the tracked entity.
        numberOfObjects:
          $ref: '#/components/schemas/UInt32Range'
          description: >-
            Estimated number of objects or units that are represented by this
            entity. Known as Strength in certain contexts (Link16)
             if UpperBound == LowerBound; (strength = LowerBound)
             If both UpperBound and LowerBound are defined; strength is between LowerBound and UpperBound (represented as string "Strength: 4-5")
             If UpperBound is defined only (LowerBound unset), Strength ≤ UpperBound
             If LowerBound is defined only (UpperBound unset), LowerBound ≤ Strength
             0 indicates unset.
        radarCrossSection:
          type: number
          format: double
          description: >-
            The radar cross section (RCS) is a measure of how detectable an
            object is by radar. A large RCS indicates an object is more easily
             detected. The unit is “decibels per square meter,” or dBsm
        lastMeasurementTime:
          type: string
          format: date-time
          description: Timestamp of the latest tracking measurement for this entity.
        lineOfBearing:
          $ref: '#/components/schemas/LineOfBearing'
          description: >-
            The relative position of a track with respect to the entity that is
            tracking it. Used for tracks that do not yet have a 3D position.
             For this entity (A), being tracked by some entity (B), this LineOfBearing would express a ray from B to A.
      description: Available for Entities that are tracked.
      title: Tracked
    PrimaryCorrelation:
      type: object
      properties:
        secondaryEntityIds:
          type: array
          items:
            type: string
          description: The secondary entity IDs part of this correlation.
      title: PrimaryCorrelation
    Provenance:
      type: object
      properties:
        integrationName:
          type: string
          description: Name of the integration that produced this entity
        dataType:
          type: string
          description: 'Source data type of this entity. Examples: ADSB, Link16, etc.'
        sourceId:
          type: string
          description: An ID that allows an element from a source to be uniquely identified
        sourceUpdateTime:
          type: string
          format: date-time
          description: >-
            The time, according to the source system, that the data in the
            entity was last modified. Generally, this should
             be the time that the source-reported time of validity of the data in the entity. This field must be
             updated with every change to the entity or else Entity Manager will discard the update.
        sourceDescription:
          type: string
          description: >-
            Description of the modification source. In the case of a user this
            is the email address.
      description: Data provenance.
      title: Provenance
    CorrelationMetadataReplicationMode:
      type: string
      enum:
        - CORRELATION_REPLICATION_MODE_INVALID
        - CORRELATION_REPLICATION_MODE_LOCAL
        - CORRELATION_REPLICATION_MODE_GLOBAL
      description: >-
        Indicates how the correlation will be distributed. Because a correlation
        is composed of
         multiple secondaries, each of which may have been correlated with different replication
         modes, the distribution of the correlation is composed of distributions of the individual
         entities within the correlation set.
         For example, if there are two secondary entities A and B correlated against a primary C,
         with A having been correlated globally and B having been correlated locally, then the
         correlation set that is distributed globally than what is known locally in the node.
      title: CorrelationMetadataReplicationMode
    CorrelationMetadataType:
      type: string
      enum:
        - CORRELATION_TYPE_INVALID
        - CORRELATION_TYPE_MANUAL
        - CORRELATION_TYPE_AUTOMATED
      description: What type of (de)correlation was this entity added with.
      title: CorrelationMetadataType
    CorrelationMetadata:
      type: object
      properties:
        provenance:
          $ref: '#/components/schemas/Provenance'
          description: Who or what added this entity to the (de)correlation.
        replicationMode:
          $ref: '#/components/schemas/CorrelationMetadataReplicationMode'
          description: >-
            Indicates how the correlation will be distributed. Because a
            correlation is composed of
             multiple secondaries, each of which may have been correlated with different replication
             modes, the distribution of the correlation is composed of distributions of the individual
             entities within the correlation set.
             For example, if there are two secondary entities A and B correlated against a primary C,
             with A having been correlated globally and B having been correlated locally, then the
             correlation set that is distributed globally than what is known locally in the node.
        type:
          $ref: '#/components/schemas/CorrelationMetadataType'
          description: What type of (de)correlation was this entity added with.
      title: CorrelationMetadata
    SecondaryCorrelation:
      type: object
      properties:
        primaryEntityId:
          type: string
          description: The primary of this correlation.
        metadata:
          $ref: '#/components/schemas/CorrelationMetadata'
          description: Metadata about the correlation.
      title: SecondaryCorrelation
    PrimaryMembership:
      type: object
      properties: {}
      title: PrimaryMembership
    NonPrimaryMembership:
      type: object
      properties: {}
      title: NonPrimaryMembership
    CorrelationMembership:
      type: object
      properties:
        correlationSetId:
          type: string
          description: The ID of the correlation set this entity belongs to.
        primary:
          $ref: '#/components/schemas/PrimaryMembership'
          description: >-
            This entity is the primary of a correlation set meaning that it
            serves as the representative
             entity of the correlation set.
        nonPrimary:
          $ref: '#/components/schemas/NonPrimaryMembership'
          description: >-
            This entity is not the primary of the correlation set. Note that
            there may not
             be a primary at all.
        metadata:
          $ref: '#/components/schemas/CorrelationMetadata'
          description: Additional metadata on this correlation.
      title: CorrelationMembership
    DecorrelatedAll:
      type: object
      properties:
        metadata:
          $ref: '#/components/schemas/CorrelationMetadata'
          description: Metadata about the decorrelation.
      title: DecorrelatedAll
    DecorrelatedSingle:
      type: object
      properties:
        entityId:
          type: string
          description: The entity that was decorrelated against.
        metadata:
          $ref: '#/components/schemas/CorrelationMetadata'
          description: Metadata about the decorrelation.
      title: DecorrelatedSingle
    Decorrelation:
      type: object
      properties:
        all:
          $ref: '#/components/schemas/DecorrelatedAll'
          description: >-
            This will be specified if this entity was decorrelated against all
            other entities.
        decorrelatedEntities:
          type: array
          items:
            $ref: '#/components/schemas/DecorrelatedSingle'
          description: >-
            A list of decorrelated entities that have been explicitly
            decorrelated against this entity
             which prevents lower precedence correlations from overriding it in the future.
             For example, if an operator in the UI decorrelated tracks A and B, any automated
             correlators would be unable to correlate them since manual decorrelations have
             higher precedence than automatic ones. Precedence is determined by both correlation
             type and replication mode.
      title: Decorrelation
    Correlation:
      type: object
      properties:
        primary:
          $ref: '#/components/schemas/PrimaryCorrelation'
          description: >-
            This entity is the primary of a correlation meaning that it serves
            as the representative
             entity of the correlation set.
        secondary:
          $ref: '#/components/schemas/SecondaryCorrelation'
          description: >-
            This entity is a secondary of a correlation meaning that it will be
            represented by the
             primary of the correlation set.
        membership:
          $ref: '#/components/schemas/CorrelationMembership'
          description: If present, this entity is a part of a correlation set.
        decorrelation:
          $ref: '#/components/schemas/Decorrelation'
          description: >-
            If present, this entity was explicitly decorrelated from one or more
            entities.
             An entity can be both correlated and decorrelated as long as they are disjoint sets.
             An example would be if a user in the UI decides that two tracks are not actually the
             same despite an automatic correlator having correlated them. The user would then
             decorrelate the two tracks and this decorrelation would be preserved preventing the
             correlator from re-correlating them at a later time.
      description: >-
        Available for Entities that are a correlated (N to 1) set of entities.
        This will be present on
         each entity in the set.
      title: Correlation
    MilViewDisposition:
      type: string
      enum:
        - DISPOSITION_UNKNOWN
        - DISPOSITION_FRIENDLY
        - DISPOSITION_HOSTILE
        - DISPOSITION_SUSPICIOUS
        - DISPOSITION_ASSUMED_FRIENDLY
        - DISPOSITION_NEUTRAL
        - DISPOSITION_PENDING
      title: MilViewDisposition
    MilViewEnvironment:
      type: string
      enum:
        - ENVIRONMENT_UNKNOWN
        - ENVIRONMENT_AIR
        - ENVIRONMENT_SURFACE
        - ENVIRONMENT_SUB_SURFACE
        - ENVIRONMENT_LAND
        - ENVIRONMENT_SPACE
      title: MilViewEnvironment
    MilViewNationality:
      type: string
      enum:
        - NATIONALITY_INVALID
        - NATIONALITY_ALBANIA
        - NATIONALITY_ALGERIA
        - NATIONALITY_ARGENTINA
        - NATIONALITY_ARMENIA
        - NATIONALITY_AUSTRALIA
        - NATIONALITY_AUSTRIA
        - NATIONALITY_AZERBAIJAN
        - NATIONALITY_BELARUS
        - NATIONALITY_BELGIUM
        - NATIONALITY_BOLIVIA
        - NATIONALITY_BOSNIA_AND_HERZEGOVINA
        - NATIONALITY_BRAZIL
        - NATIONALITY_BULGARIA
        - NATIONALITY_CAMBODIA
        - NATIONALITY_CANADA
        - NATIONALITY_CHILE
        - NATIONALITY_CHINA
        - NATIONALITY_COLOMBIA
        - NATIONALITY_CROATIA
        - NATIONALITY_CUBA
        - NATIONALITY_CYPRUS
        - NATIONALITY_CZECH_REPUBLIC
        - NATIONALITY_DEMOCRATIC_PEOPLES_REPUBLIC_OF_KOREA
        - NATIONALITY_DENMARK
        - NATIONALITY_DOMINICAN_REPUBLIC
        - NATIONALITY_ECUADOR
        - NATIONALITY_EGYPT
        - NATIONALITY_ESTONIA
        - NATIONALITY_ETHIOPIA
        - NATIONALITY_FINLAND
        - NATIONALITY_FRANCE
        - NATIONALITY_GEORGIA
        - NATIONALITY_GERMANY
        - NATIONALITY_GREECE
        - NATIONALITY_GUATEMALA
        - NATIONALITY_GUINEA
        - NATIONALITY_HUNGARY
        - NATIONALITY_ICELAND
        - NATIONALITY_INDIA
        - NATIONALITY_INDONESIA
        - NATIONALITY_INTERNATIONAL_RED_CROSS
        - NATIONALITY_IRAQ
        - NATIONALITY_IRELAND
        - NATIONALITY_ISLAMIC_REPUBLIC_OF_IRAN
        - NATIONALITY_ISRAEL
        - NATIONALITY_ITALY
        - NATIONALITY_JAMAICA
        - NATIONALITY_JAPAN
        - NATIONALITY_JORDAN
        - NATIONALITY_KAZAKHSTAN
        - NATIONALITY_KUWAIT
        - NATIONALITY_KYRGHYZ_REPUBLIC
        - NATIONALITY_LAO_PEOPLES_DEMOCRATIC_REPUBLIC
        - NATIONALITY_LATVIA
        - NATIONALITY_LEBANON
        - NATIONALITY_LIBERIA
        - NATIONALITY_LITHUANIA
        - NATIONALITY_LUXEMBOURG
        - NATIONALITY_MADAGASCAR
        - NATIONALITY_MALAYSIA
        - NATIONALITY_MALTA
        - NATIONALITY_MEXICO
        - NATIONALITY_MOLDOVA
        - NATIONALITY_MONTENEGRO
        - NATIONALITY_MOROCCO
        - NATIONALITY_MYANMAR
        - NATIONALITY_NATO
        - NATIONALITY_NETHERLANDS
        - NATIONALITY_NEW_ZEALAND
        - NATIONALITY_NICARAGUA
        - NATIONALITY_NIGERIA
        - NATIONALITY_NORWAY
        - NATIONALITY_PAKISTAN
        - NATIONALITY_PANAMA
        - NATIONALITY_PARAGUAY
        - NATIONALITY_PERU
        - NATIONALITY_PHILIPPINES
        - NATIONALITY_POLAND
        - NATIONALITY_PORTUGAL
        - NATIONALITY_REPUBLIC_OF_KOREA
        - NATIONALITY_ROMANIA
        - NATIONALITY_RUSSIA
        - NATIONALITY_SAUDI_ARABIA
        - NATIONALITY_SENEGAL
        - NATIONALITY_SERBIA
        - NATIONALITY_SINGAPORE
        - NATIONALITY_SLOVAKIA
        - NATIONALITY_SLOVENIA
        - NATIONALITY_SOUTH_AFRICA
        - NATIONALITY_SPAIN
        - NATIONALITY_SUDAN
        - NATIONALITY_SWEDEN
        - NATIONALITY_SWITZERLAND
        - NATIONALITY_SYRIAN_ARAB_REPUBLIC
        - NATIONALITY_TAIWAN
        - NATIONALITY_TAJIKISTAN
        - NATIONALITY_THAILAND
        - NATIONALITY_THE_FORMER_YUGOSLAV_REPUBLIC_OF_MACEDONIA
        - NATIONALITY_TUNISIA
        - NATIONALITY_TURKEY
        - NATIONALITY_TURKMENISTAN
        - NATIONALITY_UGANDA
        - NATIONALITY_UKRAINE
        - NATIONALITY_UNITED_KINGDOM
        - NATIONALITY_UNITED_NATIONS
        - NATIONALITY_UNITED_REPUBLIC_OF_TANZANIA
        - NATIONALITY_UNITED_STATES_OF_AMERICA
        - NATIONALITY_URUGUAY
        - NATIONALITY_UZBEKISTAN
        - NATIONALITY_VENEZUELA
        - NATIONALITY_VIETNAM
        - NATIONALITY_YEMEN
        - NATIONALITY_ZIMBABWE
      title: MilViewNationality
    MilView:
      type: object
      properties:
        disposition:
          $ref: '#/components/schemas/MilViewDisposition'
        environment:
          $ref: '#/components/schemas/MilViewEnvironment'
        nationality:
          $ref: '#/components/schemas/MilViewNationality'
      description: Provides the disposition, environment, and nationality of an Entity.
      title: MilView
    OntologyTemplate:
      type: string
      enum:
        - TEMPLATE_INVALID
        - TEMPLATE_TRACK
        - TEMPLATE_SENSOR_POINT_OF_INTEREST
        - TEMPLATE_ASSET
        - TEMPLATE_GEO
        - TEMPLATE_SIGNAL_OF_INTEREST
      description: >-
        The template used when creating this entity. Specifies minimum required
        components.
      title: OntologyTemplate
    Ontology:
      type: object
      properties:
        platformType:
          type: string
          description: >-
            A string that describes the entity's high-level type with natural
            language.
        specificType:
          type: string
          description: A string that describes the entity's exact model or type.
        template:
          $ref: '#/components/schemas/OntologyTemplate'
          description: >-
            The template used when creating this entity. Specifies minimum
            required components.
      description: Ontology of the entity.
      title: Ontology
    SensorOperationalState:
      type: string
      enum:
        - OPERATIONAL_STATE_INVALID
        - OPERATIONAL_STATE_OFF
        - OPERATIONAL_STATE_NON_OPERATIONAL
        - OPERATIONAL_STATE_DEGRADED
        - OPERATIONAL_STATE_OPERATIONAL
        - OPERATIONAL_STATE_DENIED
      title: SensorOperationalState
    SensorSensorType:
      type: string
      enum:
        - SENSOR_TYPE_INVALID
        - SENSOR_TYPE_RADAR
        - SENSOR_TYPE_CAMERA
        - SENSOR_TYPE_TRANSPONDER
        - SENSOR_TYPE_RF
        - SENSOR_TYPE_GPS
        - SENSOR_TYPE_PTU_POS
        - SENSOR_TYPE_PERIMETER
        - SENSOR_TYPE_SONAR
      description: The type of sensor
      title: SensorSensorType
    Frequency:
      type: object
      properties:
        frequencyHz:
          $ref: '#/components/schemas/Measurement'
          description: Indicates a frequency of a signal (Hz) with its standard deviation.
      description: A component for describing frequency.
      title: Frequency
    FrequencyRange:
      type: object
      properties:
        minimumFrequencyHz:
          $ref: '#/components/schemas/Frequency'
          description: Indicates the lowest measured frequency of a signal (Hz).
        maximumFrequencyHz:
          $ref: '#/components/schemas/Frequency'
          description: Indicates the maximum measured frequency of a signal (Hz).
      description: A component to represent a frequency range.
      title: FrequencyRange
    Bandwidth:
      type: object
      properties:
        bandwidthHz:
          type: number
          format: double
      description: Describes the bandwidth of a signal
      title: Bandwidth
    BandwidthRange:
      type: object
      properties:
        minimumBandwidth:
          $ref: '#/components/schemas/Bandwidth'
        maximumBandwidth:
          $ref: '#/components/schemas/Bandwidth'
      description: A component that describes the min and max bandwidths of a sensor
      title: BandwidthRange
    RFConfiguration:
      type: object
      properties:
        frequencyRangeHz:
          type: array
          items:
            $ref: '#/components/schemas/FrequencyRange'
          description: Frequency ranges that are available for this sensor.
        bandwidthRangeHz:
          type: array
          items:
            $ref: '#/components/schemas/BandwidthRange'
          description: Bandwidth ranges that are available for this sensor.
      description: Represents RF configurations supported on this sensor.
      title: RFConfiguration
    ProjectedFrustum:
      type: object
      properties:
        upperLeft:
          $ref: '#/components/schemas/Position'
          description: Upper left point of the frustum.
        upperRight:
          $ref: '#/components/schemas/Position'
          description: Upper right point of the frustum.
        bottomRight:
          $ref: '#/components/schemas/Position'
          description: Bottom right point of the frustum.
        bottomLeft:
          $ref: '#/components/schemas/Position'
          description: Bottom left point of the frustum.
      description: >-
        Represents a frustum in which which all four corner points project onto
        the ground. All points in this message
         are optional, if the projection to the ground fails then they will not be populated.
      title: ProjectedFrustum
    EntityManager.Pose:
      type: object
      properties:
        pos:
          $ref: '#/components/schemas/Position'
          description: Geospatial location defined by this Pose.
        orientation:
          $ref: '#/components/schemas/Quaternion'
          description: >-
            The quaternion to transform a point in the Pose frame to the ENU
            frame. The Pose frame could be Body, Turret,
             etc and is determined by the context in which this Pose is used.
             The normal convention for defining orientation is to list the frames of transformation, for example
             att_gimbal_to_enu is the quaternion which transforms a point in the gimbal frame to the body frame, but
             in this case we truncate to att_enu because the Pose frame isn't defined. A potentially better name for this
             field would have been att_pose_to_enu.

             Implementations of this quaternion should left multiply this quaternion to transform a point from the Pose frame
             to the enu frame.
      title: EntityManager.Pose
    FieldOfViewMode:
      type: string
      enum:
        - SENSOR_MODE_INVALID
        - SENSOR_MODE_SEARCH
        - SENSOR_MODE_TRACK
        - SENSOR_MODE_WEAPON_SUPPORT
        - SENSOR_MODE_AUTO
        - SENSOR_MODE_MUTE
      description: >-
        The mode that this sensor is currently in, used to display for context
        in the UI. Some sensors can emit multiple
         sensor field of views with different modes, for example a radar can simultaneously search broadly and perform
         tighter bounded tracking.
      title: FieldOfViewMode
    FieldOfView:
      type: object
      properties:
        fovId:
          type: integer
          description: >-
            The Id for one instance of a FieldOfView, persisted across multiple
            updates to provide continuity during
             smoothing. This is relevant for sensors where the dwell schedule is on the order of
             milliseconds, making multiple FOVs a requirement for proper display of search beams.
        mountId:
          type: string
          description: The Id of the mount the sensor is on.
        projectedFrustum:
          $ref: '#/components/schemas/ProjectedFrustum'
          description: The field of view the sensor projected onto the ground.
        projectedCenterRay:
          $ref: '#/components/schemas/Position'
          description: Center ray of the frustum projected onto the ground.
        centerRayPose:
          $ref: '#/components/schemas/EntityManager.Pose'
          description: >-
            The origin and direction of the center ray for this sensor relative
            to the ENU frame. A ray which is aligned with
             the positive X axis in the sensor frame will be transformed into the ray along the sensor direction in the ENU
             frame when transformed by the quaternion contained in this pose.
        horizontalFov:
          type: number
          format: double
          description: Horizontal field of view in radians.
        verticalFov:
          type: number
          format: double
          description: Vertical field of view in radians.
        range:
          type: number
          format: double
          description: Sensor range in meters.
        mode:
          $ref: '#/components/schemas/FieldOfViewMode'
          description: >-
            The mode that this sensor is currently in, used to display for
            context in the UI. Some sensors can emit multiple
             sensor field of views with different modes, for example a radar can simultaneously search broadly and perform
             tighter bounded tracking.
      description: Sensor Field Of View closely resembling fov.proto SensorFieldOfView.
      title: FieldOfView
    Sensor:
      type: object
      properties:
        sensorId:
          type: string
          description: >-
            This generally is used to indicate a specific type at a more
            detailed granularity. E.g. COMInt or LWIR
        operationalState:
          $ref: '#/components/schemas/SensorOperationalState'
        sensorType:
          $ref: '#/components/schemas/SensorSensorType'
          description: The type of sensor
        sensorDescription:
          type: string
          description: A human readable description of the sensor
        rfConfiguraton:
          $ref: '#/components/schemas/RFConfiguration'
          description: RF configuration details of the sensor
        lastDetectionTimestamp:
          type: string
          format: date-time
          description: Time of the latest detection from the sensor
        fieldsOfView:
          type: array
          items:
            $ref: '#/components/schemas/FieldOfView'
          description: Multiple fields of view for a single sensor component
      description: Individual sensor configuration.
      title: Sensor
